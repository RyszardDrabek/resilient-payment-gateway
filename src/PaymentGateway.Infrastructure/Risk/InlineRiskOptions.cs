namespace PaymentGateway.Infrastructure.Risk;

public sealed class InlineRiskOptions
{
    public const string SectionName = "InlineRisk";

    /// <summary>Global toggle for pre-settlement inline fraud evaluation.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Maximum time budget in milliseconds allocated for inline scoring before failing open (ADR-025).</summary>
    public int TimeoutMilliseconds { get; set; } = 50;

    /// <summary>Risk score (0-100) threshold at or above which a payment is blocked upfront without contacting acquirer.</summary>
    public int BlockThresholdScore { get; set; } = 85;

    /// <summary>Risk score (0-100) threshold at or above which 3DS2 Step-up Challenge authentication is enforced.</summary>
    public int ChallengeThresholdScore { get; set; } = 60;

    /// <summary>Maximum allowed payment requests per party in a rolling 60-second window.</summary>
    public int MaxVelocityPerMinute { get; set; } = 10;
}
