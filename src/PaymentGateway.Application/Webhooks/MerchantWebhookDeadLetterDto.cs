namespace PaymentGateway.Application.Webhooks;

public sealed record MerchantWebhookDeadLetterDto(
    Guid Id,
    string EventId,
    string PaymentId,
    string MerchantId,
    string DestinationUrl,
    string PayloadJson,
    int DeliveryAttempts,
    string LastErrorMessage,
    int? LastStatusCode,
    string Status,
    DateTimeOffset FailedAt,
    DateTimeOffset? ReplayedAt,
    int ReplayAttemptCount = 0,
    int? LastReplayStatusCode = null)
{
    public int AttemptCount => DeliveryAttempts;
    public string LastError => LastErrorMessage;
    public DateTimeOffset LastAttemptedAt => FailedAt;
}
