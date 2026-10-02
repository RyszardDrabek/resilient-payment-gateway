using System.Text.Json;
using MediatR;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Exceptions;
using PaymentGateway.Domain.Ports;
using PaymentGateway.Domain.Services;

namespace PaymentGateway.Application.Payments;

public sealed class CancelPaymentCommandHandler(
    IPaymentRepository repository,
    ISettlementPort settlementPort,
    IIdempotencyRepository idempotencyRepository) : IRequestHandler<CancelPaymentCommand, PaymentDto>
{
    public async Task<PaymentDto> Handle(CancelPaymentCommand request, CancellationToken ct)
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
            "Cancel",
            request.PaymentId,
            payment.PartyId,
            payment.Amount,
            payment.Currency,
            payment.SettlementChannel);

        var existing = await idempotencyRepository.FindAsync(request.IdempotencyKey, "Cancel", request.PaymentId, ct);
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
            record = IdempotencyRecord.CreateInFlight(request.IdempotencyKey, "Cancel", payloadHash, request.PaymentId);
            await idempotencyRepository.AddAsync(record, ct);
        }

        // AC-2, AC-4: State check before channel call — authorized & uncaptured only
        if (payment.State != PaymentState.Authorized)
        {
            record.Fail(409, "Invalid state for cancel");
            await idempotencyRepository.UpdateAsync(record, ct);
            throw new PaymentInvalidStateException(payment.State, "Cancel");
        }

        SettlementResult settlement;
        try
        {
            settlement = await settlementPort.CancelAsync(
                payment.Id,
                payment.ChannelReference ?? string.Empty,
                payment.SettlementChannel,
                ct);
        }
        catch (Exception ex)
        {
            settlement = SettlementResult.Unanswered(payment.SettlementChannel, ex.Message, payment.Id);
        }

        // AC-5: If settlement channel rejects or fails to answer, leave in prior state
        if (!settlement.IsSuccessful)
        {
            var reason = settlement.DeclineReason ?? "Settlement channel rejected cancellation request.";
            record.Fail(422, reason);
            await idempotencyRepository.UpdateAsync(record, ct);
            throw new PaymentOperationFailedException("Cancel", reason);
        }

        payment.Cancel(settlement.ChannelReference);
        await repository.UpdateAsync(payment, ct);

        var dto = PaymentMapper.ToDto(payment);
        var serializedDto = JsonSerializer.Serialize(dto);

        record.Complete(200, serializedDto, payment.Id);
        await idempotencyRepository.UpdateAsync(record, ct);

        return dto;
    }
}
