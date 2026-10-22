namespace PaymentGateway.Infrastructure.Options;

public sealed class FxRateRefreshOptions
{
    public const string SectionName = "FxRateRefresh";

    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 300;
}
