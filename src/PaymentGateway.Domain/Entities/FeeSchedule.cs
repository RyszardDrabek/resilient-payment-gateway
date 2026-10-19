namespace PaymentGateway.Domain.Entities;

public sealed class FeeSchedule
{
    public string Id { get; private set; } = string.Empty;
    public string MerchantId { get; private set; } = string.Empty;
    public string Currency { get; private set; } = string.Empty;
    public long FixedFeeMinorUnits { get; private set; }
    public int PercentageFeeBps { get; private set; }
    public DateTimeOffset EffectiveFrom { get; private set; }
    public DateTimeOffset? EffectiveTo { get; private set; }

    private FeeSchedule() { } // EF Core

    public FeeSchedule(
        string id,
        string merchantId,
        string currency,
        long fixedFeeMinorUnits,
        int percentageFeeBps,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(merchantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        ArgumentOutOfRangeException.ThrowIfNegative(fixedFeeMinorUnits);
        ArgumentOutOfRangeException.ThrowIfNegative(percentageFeeBps);

        Id = id;
        MerchantId = merchantId;
        Currency = currency.Trim().ToUpperInvariant();
        FixedFeeMinorUnits = fixedFeeMinorUnits;
        PercentageFeeBps = percentageFeeBps;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
    }

    public long CalculateFee(long amountMinorUnits)
    {
        if (amountMinorUnits <= 0)
        {
            return 0;
        }

        // Half-up rounding for basis points percentage fee: (amount * bps + 5000) / 10000
        var percentageFee = (amountMinorUnits * PercentageFeeBps + 5000) / 10000;
        return FixedFeeMinorUnits + percentageFee;
    }
}
