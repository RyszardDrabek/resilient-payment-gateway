using System.Text.Json;
using MediatR;
using PaymentGateway.Application.Metrics;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Exceptions;
using PaymentGateway.Domain.Ports;
using PaymentGateway.Domain.Services;

namespace PaymentGateway.Application.Payments;

public sealed class CapturePaymentCommandHandler(
    IPaymentRepository repository,
    ISettlementPort settlementPort,
    IIdempotencyRepository idempotencyRepository,
    IUnitOfWork? unitOfWork = null) : IRequestHandler<CapturePaymentCommand, PaymentDto>
{
    public async Task<PaymentDto> Handle(CapturePaymentCommand request, CancellationToken ct)
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

        // Pre-domain guard for early exit and validation before idempotency hash computation
        if (request.Amount.HasValue)
        {
            if (request.Amount.Value <= 0)
            {
                throw new PaymentValidationException("Capture amount must be greater than zero.");
            }

            if (payment.State is PaymentState.Authorized or PaymentState.PartiallyCaptured
                && request.Amount.Value > payment.RemainingAuthorizedAmount)
            {
                throw new PaymentValidationException($"Capture amount {request.Amount.Value} exceeds remaining authorized amount {payment.RemainingAuthorizedAmount}.");
            }
        }

        var effectiveAmount = request.Amount ?? payment.RemainingAuthorizedAmount;
        var payloadAmount = request.Amount ?? 0L;

        var payloadHash = IdempotencyPayloadHasher.ComputeHash(
            "Capture",
            request.PaymentId,
            payment.PartyId,
            payloadAmount,
            payment.Currency,
            payment.SettlementChannel);

        var existing = await idempotencyRepository.FindAsync(request.IdempotencyKey, "Capture", request.PaymentId, ct);
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
            record = IdempotencyRecord.CreateInFlight(request.IdempotencyKey, "Capture", payloadHash, request.PaymentId);
            await idempotencyRepository.AddAsync(record, ct);
        }

        // AC-4: State check before channel call
        if (payment.State != PaymentState.Authorized && payment.State != PaymentState.PartiallyCaptured)
        {
            record.Fail(409, "Invalid state for capture");
            await idempotencyRepository.UpdateAsync(record, ct);
            throw new PaymentInvalidStateException(payment.State, "Capture");
        }

        SettlementResult settlement;
        try
        {
            settlement = await settlementPort.CaptureAsync(
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
        // Move payment to Pending transition state, retain tx hash, and do not report as captured.
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
            var reason = settlement.DeclineReason ?? "Settlement channel rejected capture request.";
            record.Fail(409, reason);
            await idempotencyRepository.UpdateAsync(record, ct);
            throw new PaymentOperationFailedException("Capture", reason);
        }

        // AC-1: Move to captured/partially captured state with channel reference
        payment.Capture(settlement.ChannelReference, effectiveAmount);

        PaymentMetrics.RecordTransaction(payment.State.ToString(), payment.SettlementChannel ?? "UNKNOWN", "capture");
        PaymentMetrics.RecordVolume(effectiveAmount, payment.Currency, payment.SettlementChannel ?? "UNKNOWN", "capture", payment.State.ToString());

        var dto = PaymentMapper.ToDto(payment);
        var serializedDto = JsonSerializer.Serialize(dto);

        async Task CommitCaptureAsync(CancellationToken token)
        {
            await repository.UpdateAsync(payment, token);
            record.Complete(200, serializedDto, payment.Id);
            await idempotencyRepository.UpdateAsync(record, token);
        }

        if (unitOfWork is not null)
        {
            await unitOfWork.ExecuteTransactionAsync(CommitCaptureAsync, ct);
        }
        else
        {
            await CommitCaptureAsync(ct);
        }

        return dto;
    }
}
