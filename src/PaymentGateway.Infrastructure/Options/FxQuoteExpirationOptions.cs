namespace PaymentGateway.Infrastructure.Options;

public sealed class FxQuoteExpirationOptions
{
    public const string SectionName = "FxQuoteExpiration";

    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 30;
    public int BatchSize { get; set; } = 1000;
}
