namespace PaymentGateway.Application.Fx.Ports;

public interface IFxRateProvider
{
    Task<decimal> GetSpotRateAsync(string baseCurrency, string targetCurrency, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, decimal>> GetRatesAsync(string baseCurrency, CancellationToken ct = default);
}
