namespace PaymentGateway.Application.Webhooks;

public sealed record MerchantWebhookPayload(
    string EventId,
    string PaymentId,
    string State,
    long Amount,
    string Currency,
    DateTimeOffset OccurredAt);
