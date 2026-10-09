using System.Text.Json;
using MediatR;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Exceptions;
using PaymentGateway.Domain.Ports;
using PaymentGateway.Domain.Services;

namespace PaymentGateway.Application.Payments;

public sealed class AuthorizePaymentCommandHandler(
    ISettlementPort settlementPort,
    IPaymentRepository repository,
    IIdempotencyRepository idempotencyRepository) : IRequestHandler<AuthorizePaymentCommand, PaymentDto>
{
    public async Task<PaymentDto> Handle(AuthorizePaymentCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new IdempotencyKeyMissingException();
        }

        var payloadHash = IdempotencyPayloadHasher.ComputeHash(
            "Authorize",
            null,
            request.PartyId,
            request.Amount,
            request.Currency,
            request.SettlementChannel);

        var existing = await idempotencyRepository.FindAsync(request.IdempotencyKey, "Authorize", null, ct);
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

                if (!string.IsNullOrWhiteSpace(existing.PaymentId))
                {
                    var paymentEntity = await repository.GetByIdAsync(existing.PaymentId, ct);
                    if (paymentEntity is not null)
                    {
                        return PaymentMapper.ToDto(paymentEntity);
                    }
                }
            }

            // InFlight with expired lease or retryable state: re-acquire lease
            existing.RenewLease(DateTimeOffset.UtcNow);
            await idempotencyRepository.UpdateAsync(existing, ct);
            record = existing;
        }
        else
        {
            record = IdempotencyRecord.CreateInFlight(request.IdempotencyKey, "Authorize", payloadHash);
            await idempotencyRepository.AddAsync(record, ct);
        }

        SettlementResult settlement;
        try
        {
            settlement = await settlementPort.AuthorizeAsync(
                request.PartyId,
                request.Amount,
                request.Currency,
                request.SettlementChannel,
                request.IdempotencyKey,
                ct);
        }
        catch (Exception ex)
        {
            settlement = SettlementResult.Unanswered(
                request.SettlementChannel ?? "UNKNOWN",
                $"Settlement channel failed to answer: {ex.Message}");
        }

        var channel = settlement.Channel;

        var payment = settlement.IsAuthorized
            ? Payment.Authorize(request.PartyId, request.Amount, request.Currency, channel, settlement.ChannelReference)
            : settlement.IsUnanswered
                ? Payment.CreatePending(request.PartyId, request.Amount, request.Currency, channel, settlement.ChannelReference, settlement.DeclineReason)
                : Payment.Decline(request.PartyId, request.Amount, request.Currency, channel, settlement.ChannelReference, settlement.DeclineReason);

        await repository.AddAsync(payment, ct);

        var dto = PaymentMapper.ToDto(payment);
        var serializedDto = JsonSerializer.Serialize(dto);

        record.Complete(201, serializedDto, payment.Id);
        await idempotencyRepository.UpdateAsync(record, ct);

        return dto;
    }
}
