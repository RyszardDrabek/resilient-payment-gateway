namespace PaymentGateway.Application.Reconciliation;

public interface IFeeCalculator
{
    Task<long> CalculateFeeAsync(
        string merchantId,
        string currency,
        long amountMinorUnits,
        DateTimeOffset asOf,
        CancellationToken ct = default);
}
