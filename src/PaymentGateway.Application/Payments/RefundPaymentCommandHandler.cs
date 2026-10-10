using System.Text.Json;
using MediatR;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Application.Metrics;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Exceptions;
using PaymentGateway.Domain.Ports;
using PaymentGateway.Domain.Services;

namespace PaymentGateway.Application.Payments;

public sealed class RefundPaymentCommandHandler(
    IPaymentRepository repository,
    ISettlementPort settlementPort,
    IIdempotencyRepository idempotencyRepository) : IRequestHandler<RefundPaymentCommand, PaymentDto>
{
    public async Task<PaymentDto> Handle(RefundPaymentCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new IdempotencyKeyMissingException();
        }

        var payment = await repository.GetByIdAsync(request.PaymentId, ct);
        if (payment is null)
        {
            throw new PaymentNotFoundException(request.PaymentId);
        }

        var payloadHash = IdempotencyPayloadHasher.ComputeHash(
            "Refund",
            request.PaymentId,
            payment.PartyId,
            payment.Amount,
            payment.Currency,
            payment.SettlementChannel);

        var existing = await idempotencyRepository.FindAsync(request.IdempotencyKey, "Refund", request.PaymentId, ct);
        IdempotencyRecord record;

        if (existing is not null && !existing.IsExpired(DateTimeOffset.UtcNow))
        {
            if (!existing.MatchesPayload(payloadHash))
            {
                throw new IdempotencyConflictException();
            }

            if (existing.Status == IdempotencyStatus.InFlight && existing.IsLeaseActive(DateTimeOffset.UtcNow))
            {
                throw new IdempotencyInFlightException();
            }

            if (existing.Status == IdempotencyStatus.Completed)
            {
                if (!string.IsNullOrWhiteSpace(existing.ResponsePayload))
                {
                    var cachedDto = JsonSerializer.Deserialize<PaymentDto>(existing.ResponsePayload);
                    if (cachedDto is not null)
                    {
                        return cachedDto;
                    }
                }

                return PaymentMapper.ToDto(payment);
            }

            existing.RenewLease(DateTimeOffset.UtcNow);
            await idempotencyRepository.UpdateAsync(existing, ct);
            record = existing;
        }
        else
        {
            record = IdempotencyRecord.CreateInFlight(request.IdempotencyKey, "Refund", payloadHash, request.PaymentId);
            await idempotencyRepository.AddAsync(record, ct);
        }

        // AC-3, AC-4: State check before channel call — captured payments only
        if (payment.State != PaymentState.Captured)
        {
            record.Fail(409, "Invalid state for refund");
            await idempotencyRepository.UpdateAsync(record, ct);
            throw new PaymentInvalidStateException(payment.State, "Refund");
        }

        SettlementResult settlement;
        try
        {
            settlement = await settlementPort.RefundAsync(
                payment.Id,
                payment.ChannelReference ?? string.Empty,
                payment.Amount,
                payment.Currency,
                payment.SettlementChannel,
                ct);
        }
        catch (Exception ex)
        {
            settlement = SettlementResult.Unanswered(payment.SettlementChannel, ex.Message, payment.Id);
        }

        // F-WEB3-02 AC-1: On-chain settlement broadcast submitted but awaiting finality confirmation.
        // Move payment to Pending transition state, retain tx hash, and do not report as refunded.
        if (settlement.IsUnanswered && !string.IsNullOrWhiteSpace(settlement.ChannelReference))
        {
            payment.ApplyAcquirerOutcome(PaymentLifecycleOutcome.Pending, settlement.ChannelReference);
            await repository.UpdateAsync(payment, ct);

            var pendingDto = PaymentMapper.ToDto(payment);
            var serializedPending = JsonSerializer.Serialize(pendingDto);
            record.Complete(200, serializedPending, payment.Id);
            await idempotencyRepository.UpdateAsync(record, ct);

            return pendingDto;
        }

        // AC-5: If settlement channel rejects or fails to answer, leave in prior state
        if (!settlement.IsSuccessful)
        {
            var reason = settlement.DeclineReason ?? "Settlement channel rejected refund request.";
            record.Fail(409, reason);
            await idempotencyRepository.UpdateAsync(record, ct);
            throw new PaymentOperationFailedException("Refund", reason);
        }

        payment.Refund(settlement.ChannelReference);
        await repository.UpdateAsync(payment, ct);

        PaymentMetrics.RecordTransaction(payment.State.ToString(), payment.SettlementChannel ?? "UNKNOWN", "refund");

        var dto = PaymentMapper.ToDto(payment);
        var serializedDto = JsonSerializer.Serialize(dto);

        record.Complete(200, serializedDto, payment.Id);
        await idempotencyRepository.UpdateAsync(record, ct);

        return dto;
    }
}
