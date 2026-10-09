namespace PaymentGateway.Infrastructure.Risk;

public sealed class RiskOptions
{
    public const string SectionName = "Risk";

    /// <summary>Time budget in seconds for scoring an event before failing open (ADR-009).</summary>
    public int ScoreTimeoutSeconds { get; set; } = 5;

    /// <summary>Multiplier threshold over party historical average amount to trigger anomaly flag.</summary>
    public double AnomalyAmountMultiplier { get; set; } = 5.0;

    /// <summary>Absolute amount threshold in minor units considered anomalous for new parties.</summary>
    public long NewPartyAnomalyAmountThreshold { get; set; } = 10_000_000; // e.g. 100,000 EUR
}
