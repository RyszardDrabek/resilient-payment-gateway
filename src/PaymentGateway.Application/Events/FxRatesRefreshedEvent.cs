namespace PaymentGateway.Application.Events;

public sealed record FxRatesRefreshedEvent(
    Dictionary<string, decimal> RatesToUsd,
    DateTimeOffset RefreshedAt)
{
    public const string EventType = "fx.rates.refreshed";
}
