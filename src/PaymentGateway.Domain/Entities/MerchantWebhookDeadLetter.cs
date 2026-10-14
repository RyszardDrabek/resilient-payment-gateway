namespace PaymentGateway.Domain.Entities;

public sealed class MerchantWebhookDeadLetter
{
    public Guid Id { get; private set; }
    public string EventId { get; private set; } = string.Empty;
    public string PaymentId { get; private set; } = string.Empty;
    public string MerchantId { get; private set; } = string.Empty;
    public string DestinationUrl { get; private set; } = string.Empty;
    public string PayloadJson { get; private set; } = string.Empty;
    public int DeliveryAttempts { get; private set; }
    public string LastErrorMessage { get; private set; } = string.Empty;
    public int? LastStatusCode { get; private set; }
    public string Status { get; private set; } = "exhausted";
    public DateTimeOffset FailedAt { get; private set; }
    public DateTimeOffset? ReplayedAt { get; private set; }

    private MerchantWebhookDeadLetter() { } // EF Core

    public static MerchantWebhookDeadLetter Create(
        string eventId,
        string paymentId,
        string merchantId,
        string destinationUrl,
        string payloadJson,
        int deliveryAttempts,
        string lastErrorMessage,
        int? lastStatusCode = null,
        DateTimeOffset? failedAt = null)
    {
        if (string.IsNullOrWhiteSpace(eventId)) throw new ArgumentException("EventId cannot be empty.", nameof(eventId));
        if (string.IsNullOrWhiteSpace(paymentId)) throw new ArgumentException("PaymentId cannot be empty.", nameof(paymentId));
        if (string.IsNullOrWhiteSpace(merchantId)) throw new ArgumentException("MerchantId cannot be empty.", nameof(merchantId));
        if (string.IsNullOrWhiteSpace(destinationUrl)) throw new ArgumentException("DestinationUrl cannot be empty.", nameof(destinationUrl));

        return new MerchantWebhookDeadLetter
        {
            Id = Guid.NewGuid(),
            EventId = eventId.Trim(),
            PaymentId = paymentId.Trim(),
            MerchantId = merchantId.Trim(),
            DestinationUrl = destinationUrl.Trim(),
            PayloadJson = payloadJson ?? string.Empty,
            DeliveryAttempts = deliveryAttempts,
            LastErrorMessage = lastErrorMessage ?? "Delivery attempts exhausted.",
            LastStatusCode = lastStatusCode,
            Status = "exhausted",
            FailedAt = failedAt ?? DateTimeOffset.UtcNow,
            ReplayedAt = null
        };
    }

    public void MarkReplayed(DateTimeOffset? replayedAt = null)
    {
        Status = "replayed";
        ReplayedAt = replayedAt ?? DateTimeOffset.UtcNow;
    }
}
