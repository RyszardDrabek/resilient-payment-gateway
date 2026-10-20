using PaymentGateway.Domain.Enums;

namespace PaymentGateway.Domain.Entities;

public sealed class CurrencyQuote
{
    public string Id { get; private set; } = string.Empty;
    public string? PartyId { get; private set; }
    public string BaseCurrency { get; private set; } = string.Empty;
    public long BaseAmount { get; private set; }
    public string TargetCurrency { get; private set; } = string.Empty;
    public long TargetAmount { get; private set; }
    public decimal SpotRate { get; private set; }
    public int SpreadBps { get; private set; }
    public decimal ExchangeRate { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public CurrencyQuoteStatus Status { get; private set; }
    public string? ConsumedByPaymentId { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }

    private CurrencyQuote() { } // EF Core

    public CurrencyQuote(
        string id,
        string? partyId,
        string baseCurrency,
        long baseAmount,
        string targetCurrency,
        long targetAmount,
        decimal spotRate,
        int spreadBps,
        decimal exchangeRate,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        CurrencyQuoteStatus status = CurrencyQuoteStatus.Active,
        string? consumedByPaymentId = null,
        DateTimeOffset? consumedAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseCurrency);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetCurrency);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(baseAmount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetAmount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(spotRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exchangeRate);
        ArgumentOutOfRangeException.ThrowIfNegative(spreadBps);

        Id = id;
        PartyId = partyId;
        BaseCurrency = baseCurrency.Trim().ToUpperInvariant();
        BaseAmount = baseAmount;
        TargetCurrency = targetCurrency.Trim().ToUpperInvariant();
        TargetAmount = targetAmount;
        SpotRate = spotRate;
        SpreadBps = spreadBps;
        ExchangeRate = exchangeRate;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        Status = status;
        ConsumedByPaymentId = consumedByPaymentId;
        ConsumedAt = consumedAt;
    }

    public static CurrencyQuote Create(
        string id,
        string? partyId,
        string baseCurrency,
        long baseAmount,
        string targetCurrency,
        decimal spotRate,
        int spreadBps,
        TimeSpan ttl,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseCurrency);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetCurrency);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(baseAmount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(spotRate);
        ArgumentOutOfRangeException.ThrowIfNegative(spreadBps);

        // Effective exchange rate with spread margin applied
        // Example: Base = PLN, Target = USDC. SpotRate = 0.25 (1 PLN = 0.25 USDC).
        // Spread 50 bps (0.50%): customer pays slightly more target currency,
        // so effective rate of PLN to USDC is adjusted: SpotRate * (1 - Spread) => 0.24875 USDC/PLN.
        // TargetAmount = BaseAmount * EffectiveRate.
        var spreadMultiplier = 1m - (spreadBps / 10000m);
        var effectiveRate = Math.Round(spotRate * spreadMultiplier, 6, MidpointRounding.AwayFromZero);
        if (effectiveRate <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(spreadBps), "Spread cannot exceed or equal 100%.");
        }

        var targetAmount = (long)Math.Round(baseAmount * effectiveRate, MidpointRounding.AwayFromZero);
        if (targetAmount <= 0)
        {
            targetAmount = 1;
        }

        var expiresAt = now.Add(ttl);

        return new CurrencyQuote(
            id: id,
            partyId: partyId,
            baseCurrency: baseCurrency,
            baseAmount: baseAmount,
            targetCurrency: targetCurrency,
            targetAmount: targetAmount,
            spotRate: spotRate,
            spreadBps: spreadBps,
            exchangeRate: effectiveRate,
            createdAt: now,
            expiresAt: expiresAt,
            status: CurrencyQuoteStatus.Active);
    }

    public bool IsValid(DateTimeOffset now) =>
        Status == CurrencyQuoteStatus.Active && now < ExpiresAt;

    public void Consume(string paymentId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paymentId);

        if (Status == CurrencyQuoteStatus.Consumed && ConsumedByPaymentId == paymentId)
        {
            return; // Idempotent re-consumption by same payment
        }

        if (Status != CurrencyQuoteStatus.Active)
        {
            throw new InvalidOperationException($"Quote '{Id}' is not active (current status: {Status}).");
        }

        if (now >= ExpiresAt)
        {
            Status = CurrencyQuoteStatus.Expired;
            throw new InvalidOperationException($"Quote '{Id}' expired at {ExpiresAt:O}.");
        }

        Status = CurrencyQuoteStatus.Consumed;
        ConsumedByPaymentId = paymentId;
        ConsumedAt = now;
    }

    public void Expire(DateTimeOffset now)
    {
        if (Status == CurrencyQuoteStatus.Active && now >= ExpiresAt)
        {
            Status = CurrencyQuoteStatus.Expired;
        }
    }
}
