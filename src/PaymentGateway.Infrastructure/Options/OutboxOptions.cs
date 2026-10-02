namespace PaymentGateway.Infrastructure.Options;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    public int PollingIntervalMs { get; set; } = 100;
    public int BatchSize { get; set; } = 50;
    public int MaxDeliveryAttempts { get; set; } = 5;
}
