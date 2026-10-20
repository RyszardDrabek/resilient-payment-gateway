using System.Text.Json;
using MediatR;
using PaymentGateway.Application.Metrics;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Exceptions;
using PaymentGateway.Domain.Ports;
using PaymentGateway.Domain.Services;

namespace PaymentGateway.Application.Payments;

public sealed class RefundPaymentCommandHandler(
    IPaymentRepository repository,
    ISettlementPort settlementPort,
    IIdempotencyRepository idempotencyRepository,
    IUnitOfWork? unitOfWork = null) : IRequestHandler<RefundPaymentCommand, PaymentDto>
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

        var captured = payment.CapturedAmount > 0
            ? payment.CapturedAmount
            : (payment.State == PaymentState.Captured ? payment.Amount : 0L);
        var availableRefundable = Math.Max(0L, captured - payment.RefundedAmount);
        var isRemainderCancelled = payment.State == PaymentState.Cancelled && captured > 0;

        // Pre-domain guard for early exit and validation before idempotency hash computation
        if (request.Amount.HasValue)
        {
            if (request.Amount.Value <= 0)
            {
                throw new PaymentValidationException("Refund amount must be greater than zero.");
            }

            if ((payment.State is PaymentState.Captured or PaymentState.PartiallyRefunded || isRemainderCancelled)
                && request.Amount.Value > availableRefundable)
            {
                throw new PaymentValidationException($"Refund amount {request.Amount.Value} exceeds remaining refundable balance {availableRefundable}.");
            }
        }

        var effectiveAmount = request.Amount ?? availableRefundable;
        var payloadAmount = request.Amount ?? 0L;

        var payloadHash = IdempotencyPayloadHasher.ComputeHash(
            "Refund",
            request.PaymentId,
            payment.PartyId,
            payloadAmount,
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

        // AC-5: If payment has zero captured balance or is in an uncaptured state, reject with conflict status
        if (payment.State is not (PaymentState.Captured or PaymentState.PartiallyRefunded) && !isRemainderCancelled)
        {
            record.Fail(409, "Invalid state for refund");
            await idempotencyRepository.UpdateAsync(record, ct);
            throw new PaymentInvalidStateException(payment.State, "Refund");
        }

        if (availableRefundable <= 0)
        {
            record.Fail(409, "No refundable balance remaining");
            await idempotencyRepository.UpdateAsync(record, ct);
            throw new PaymentInvalidStateException(payment.State, "Refund");
        }

        SettlementResult settlement;
        try
        {
            settlement = await settlementPort.RefundAsync(
                payment.Id,
                payment.ChannelReference ?? string.Empty,
                effectiveAmount,
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

            var pendingDto = PaymentMapper.ToDto(payment);
            var serializedPending = JsonSerializer.Serialize(pendingDto);

            async Task CommitPendingAsync(CancellationToken token)
            {
                await repository.UpdateAsync(payment, token);
                record.Complete(200, serializedPending, payment.Id);
                await idempotencyRepository.UpdateAsync(record, token);
            }

            if (unitOfWork is not null)
            {
                await unitOfWork.ExecuteTransactionAsync(CommitPendingAsync, ct);
            }
            else
            {
                await CommitPendingAsync(ct);
            }

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

        payment.Refund(settlement.ChannelReference, effectiveAmount);

        PaymentMetrics.RecordTransaction(payment.State.ToString(), payment.SettlementChannel ?? "UNKNOWN", "refund");
        PaymentMetrics.RecordVolume(effectiveAmount, payment.Currency, payment.SettlementChannel ?? "UNKNOWN", "refund", payment.State.ToString());

        var dto = PaymentMapper.ToDto(payment);
        var serializedDto = JsonSerializer.Serialize(dto);

        async Task CommitRefundAsync(CancellationToken token)
        {
            await repository.UpdateAsync(payment, token);
            record.Complete(200, serializedDto, payment.Id);
            await idempotencyRepository.UpdateAsync(record, token);
        }

        if (unitOfWork is not null)
        {
            await unitOfWork.ExecuteTransactionAsync(CommitRefundAsync, ct);
        }
        else
        {
            await CommitRefundAsync(ct);
        }

        return dto;
    }
}
