namespace PaymentGateway.Edge.Options;

/// <summary>
/// Configuration options for edge rate limiting and overload protection (ADR-007).
/// </summary>
public sealed class PaymentRateLimitOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>
    /// Permitted requests per minute per caller partition (JWT sub or IP). Default: 100.
    /// </summary>
    public int PermitLimitPerMinute { get; set; } = 100;

    /// <summary>
    /// Global in-flight payment request concurrency cap for overload protection. Default: 50.
    /// </summary>
    public int GlobalConcurrencyLimit { get; set; } = 50;

    /// <summary>
    /// Queue limit for excess requests. Default: 0 (reject immediately on overload/excess rather than unbounded queueing).
    /// </summary>
    public int QueueLimit { get; set; } = 0;
}
