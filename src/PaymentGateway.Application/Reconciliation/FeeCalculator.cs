using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Application.Reconciliation;

public sealed class FeeCalculator(IFeeScheduleRepository feeScheduleRepository) : IFeeCalculator
{
    // Default fallback fee: 10 minor units fixed + 120 bps (1.20%)
    private const long DefaultFixedFee = 10;
    private const int DefaultPercentageBps = 120;

    public async Task<long> CalculateFeeAsync(
        string merchantId,
        string currency,
        long amountMinorUnits,
        DateTimeOffset asOf,
        CancellationToken ct = default)
    {
        if (amountMinorUnits <= 0)
        {
            return 0;
        }

        var schedule = await feeScheduleRepository.GetActiveFeeScheduleAsync(merchantId, currency, asOf, ct);
        if (schedule is not null)
        {
            return schedule.CalculateFee(amountMinorUnits);
        }

        // Half-up rounding for basis points percentage fee: (amount * bps + 5000) / 10000
        var percentageFee = (amountMinorUnits * DefaultPercentageBps + 5000) / 10000;
        return DefaultFixedFee + percentageFee;
    }
}
