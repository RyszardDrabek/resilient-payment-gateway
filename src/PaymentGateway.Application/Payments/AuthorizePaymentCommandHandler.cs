using System.Text.Json;
using MediatR;
using PaymentGateway.Application.Metrics;
using PaymentGateway.Application.Risk.Models;
using PaymentGateway.Application.Risk.Ports;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Exceptions;
using PaymentGateway.Domain.Ports;
using PaymentGateway.Domain.Services;

namespace PaymentGateway.Application.Payments;

public sealed class AuthorizePaymentCommandHandler(
    ISettlementPort settlementPort,
    IPaymentRepository repository,
    IIdempotencyRepository idempotencyRepository,
    IPaymentMethodRepository? paymentMethodRepository = null,
    IInlineRiskEvaluator? inlineRiskEvaluator = null,
    IRiskFlagRepository? riskFlagRepository = null,
    IFxQuoteRepository? fxQuoteRepository = null,
    IUnitOfWork? unitOfWork = null) : IRequestHandler<AuthorizePaymentCommand, PaymentDto>
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

        string? storedTokenString = null;
        if (!string.IsNullOrWhiteSpace(request.PaymentMethodId) && paymentMethodRepository is not null)
        {
            var storedMethod = await paymentMethodRepository.GetByIdAsync(request.PaymentMethodId, ct);
            if (storedMethod is null || storedMethod.Status != PaymentMethodStatus.Active)
            {
                throw new InvalidOperationException($"Payment method '{request.PaymentMethodId}' not found or is revoked.");
            }

            if (storedMethod.IsExpired())
            {
                throw new InvalidOperationException($"Payment method '{request.PaymentMethodId}' is expired.");
            }

            storedTokenString = storedMethod.Token;
        }

        CurrencyQuote? quote = null;
        if (!string.IsNullOrWhiteSpace(request.QuoteId) && fxQuoteRepository is not null)
        {
            quote = await fxQuoteRepository.GetByIdAsync(request.QuoteId, ct);
            if (quote is null)
            {
                throw new QuoteNotFoundException(request.QuoteId);
            }

            var now = DateTimeOffset.UtcNow;
            if (quote.Status == CurrencyQuoteStatus.Consumed)
            {
                if (existing is null || quote.ConsumedByPaymentId != existing.PaymentId)
                {
                    throw new QuoteAlreadyConsumedException(quote.Id, quote.ConsumedByPaymentId ?? "unknown");
                }
            }
            else if (quote.Status == CurrencyQuoteStatus.Expired || now >= quote.ExpiresAt)
            {
                quote.Expire(now);
                await fxQuoteRepository.UpdateAsync(quote, ct);
                throw new QuoteExpiredException(quote.Id, quote.ExpiresAt);
            }

            if (!string.Equals(quote.TargetCurrency, request.Currency.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new QuoteMismatchException($"Quote target currency '{quote.TargetCurrency}' does not match payment request currency '{request.Currency}'.");
            }

            if (quote.TargetAmount != request.Amount)
            {
                throw new QuoteMismatchException($"Quote target amount '{quote.TargetAmount}' does not match payment request amount '{request.Amount}'.");
            }
        }

        var isMit = string.Equals(request.TransactionOrigin, "MIT", StringComparison.OrdinalIgnoreCase);
        var originEnum = isMit ? TransactionOrigin.Mit : TransactionOrigin.Cit;

        if (inlineRiskEvaluator is not null)
        {
            var inlineRisk = await inlineRiskEvaluator.EvaluateAsync(
                request.PartyId,
                request.Amount,
                request.Currency,
                request.SettlementChannel,
                request.PaymentMethodId,
                ct);

            if (inlineRisk.Decision == InlineRiskDecision.Block)
            {
                var blockedChannel = request.SettlementChannel ?? "INLINE_RISK";
                var declineReason = $"FRAUD_BLOCKED: {inlineRisk.Reason}";
                var blockedPayment = Payment.Decline(request.PartyId, request.Amount, request.Currency, blockedChannel, null, declineReason);
                blockedPayment.SetRecurringDetails(originEnum, request.PaymentMethodId, request.SubscriptionId);

                if (quote is not null)
                {
                    blockedPayment.SetQuoteDetails(quote.Id, quote.BaseAmount, quote.BaseCurrency, quote.ExchangeRate);
                }

                var blockedDto = PaymentMapper.ToDto(blockedPayment);
                var serializedBlockedDto = JsonSerializer.Serialize(blockedDto);

                async Task CommitBlockedAsync(CancellationToken token)
                {
                    await repository.AddAsync(blockedPayment, token);

                    if (quote is not null)
                    {
                        quote.Consume(blockedPayment.Id, DateTimeOffset.UtcNow);
                        await fxQuoteRepository!.UpdateAsync(quote, token);
                    }

                    record.Complete(201, serializedBlockedDto, blockedPayment.Id);
                    await idempotencyRepository.UpdateAsync(record, token);
                }

                if (unitOfWork is not null)
                {
                    await unitOfWork.ExecuteTransactionAsync(CommitBlockedAsync, ct);
                }
                else
                {
                    await CommitBlockedAsync(ct);
                }

                if (riskFlagRepository is not null)
                {
                    var flag = new RiskFlag(
                        Guid.NewGuid().ToString(),
                        blockedPayment.Id,
                        blockedPayment.PartyId,
                        $"pre_settlement_{blockedPayment.Id}",
                        $"Inline risk blocked: {inlineRisk.Reason} (score: {inlineRisk.RiskScore})",
                        DateTimeOffset.UtcNow);
                    await riskFlagRepository.SaveAsync(flag, ct);
                }

                PaymentMetrics.RecordTransaction(blockedPayment.State.ToString(), blockedChannel, "authorize");
                PaymentMetrics.RecordDecline("FRAUD_BLOCKED", blockedChannel);

                return blockedDto;
            }
        }

        SettlementResult settlement;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            settlement = await settlementPort.AuthorizeAsync(
                request.PartyId,
                request.Amount,
                request.Currency,
                request.SettlementChannel,
                request.IdempotencyKey,
                storedTokenString,
                isMit,
                request.StorePaymentMethod,
                ct);
        }
        catch (Exception ex)
        {
            settlement = SettlementResult.Unanswered(
                request.SettlementChannel ?? "UNKNOWN",
                $"Settlement channel failed to answer: {ex.Message}");
        }
        finally
        {
            sw.Stop();
        }

        var channel = settlement.Channel;

        var payment = settlement.IsAuthorized
            ? Payment.Authorize(request.PartyId, request.Amount, request.Currency, channel, settlement.ChannelReference)
            : settlement.IsActionRequired
                ? Payment.CreateRequiresAction(request.PartyId, request.Amount, request.Currency, channel, settlement.ChannelReference, settlement.ThreeDsAction!)
                : settlement.IsUnanswered
                    ? Payment.CreatePending(request.PartyId, request.Amount, request.Currency, channel, settlement.ChannelReference, settlement.DeclineReason)
                    : Payment.Decline(request.PartyId, request.Amount, request.Currency, channel, settlement.ChannelReference, settlement.DeclineReason);

        payment.SetRecurringDetails(originEnum, request.PaymentMethodId, request.SubscriptionId);

        if (quote is not null)
        {
            payment.SetQuoteDetails(quote.Id, quote.BaseAmount, quote.BaseCurrency, quote.ExchangeRate);
        }

        PaymentMethodToken? newMethod = null;
        if (request.StorePaymentMethod &&
            settlement.IsAuthorized &&
            !string.IsNullOrWhiteSpace(settlement.StoredPaymentMethodId) &&
            paymentMethodRepository is not null)
        {
            newMethod = new PaymentMethodToken(
                $"pm_{Guid.NewGuid():N}",
                request.PartyId,
                payment.SettlementChannel,
                "visa",
                "**** **** **** 1111",
                12,
                DateTimeOffset.UtcNow.Year + 3,
                settlement.StoredPaymentMethodId);

            payment.SetRecurringDetails(originEnum, newMethod.Id, request.SubscriptionId);
        }

        var dto = PaymentMapper.ToDto(payment);
        var serializedDto = JsonSerializer.Serialize(dto);

        async Task CommitAuthorizedAsync(CancellationToken token)
        {
            if (newMethod is not null && paymentMethodRepository is not null)
            {
                await paymentMethodRepository.SaveAsync(newMethod, token);
            }

            await repository.AddAsync(payment, token);

            if (quote is not null)
            {
                quote.Consume(payment.Id, DateTimeOffset.UtcNow);
                await fxQuoteRepository!.UpdateAsync(quote, token);
            }

            record.Complete(201, serializedDto, payment.Id);
            await idempotencyRepository.UpdateAsync(record, token);
        }

        if (unitOfWork is not null)
        {
            await unitOfWork.ExecuteTransactionAsync(CommitAuthorizedAsync, ct);
        }
        else
        {
            await CommitAuthorizedAsync(ct);
        }

        PaymentMetrics.RecordTransaction(payment.State.ToString(), channel ?? "UNKNOWN", "authorize");
        PaymentMetrics.RecordChannelDuration(sw.Elapsed.TotalSeconds, channel ?? "UNKNOWN", "authorize", payment.State.ToString());
        PaymentMetrics.RecordVolume(payment.Amount, payment.Currency, channel ?? "UNKNOWN", "authorize", payment.State.ToString());

        if (payment.State == PaymentState.Declined)
        {
            PaymentMetrics.RecordDecline(settlement.DeclineReason ?? "Declined", channel ?? "UNKNOWN");
        }

        return dto;
    }
}
