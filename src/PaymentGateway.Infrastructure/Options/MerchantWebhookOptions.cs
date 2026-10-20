namespace PaymentGateway.Infrastructure.Options;

public sealed class MerchantWebhookOptions
{
    public const string SectionName = "MerchantWebhooks";

    public string? DefaultEndpointUrl { get; set; }
    public string? DefaultSecretKey { get; set; }
    public int MaxRetryAttempts { get; set; } = 5;
    public double InitialBackoffSeconds { get; set; } = 2.0;
    public int TimeoutSeconds { get; set; } = 5;
    public int DeadLetterRetentionDays { get; set; } = 90;
    public int RetentionCleanupIntervalHours { get; set; } = 24;
}
