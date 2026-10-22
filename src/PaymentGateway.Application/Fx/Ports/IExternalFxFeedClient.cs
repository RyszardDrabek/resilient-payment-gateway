namespace PaymentGateway.Application.Fx.Ports;

public interface IExternalFxFeedClient
{
    Task<IReadOnlyDictionary<string, decimal>?> FetchRatesToUsdAsync(CancellationToken ct = default);
}
