namespace PaymentGateway.Infrastructure.Options;

public sealed class IdempotencyRetentionOptions
{
    public const string SectionName = "IdempotencyRetention";

    public bool Enabled { get; set; } = true;
    public int CleanupIntervalMinutes { get; set; } = 15;
}
