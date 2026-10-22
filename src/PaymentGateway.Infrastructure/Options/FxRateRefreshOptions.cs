namespace PaymentGateway.Infrastructure.Options;

public sealed class FxRateRefreshOptions
{
    public const string SectionName = "FxRateRefresh";

    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 300;
    public string NbpApiUrl { get; set; } = "https://api.nbp.pl/api/exchangerates/tables/A/?format=json";
    public int TimeoutSeconds { get; set; } = 10;
}
