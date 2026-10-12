namespace PaymentGateway.Application.Webhooks;

public sealed record MerchantWebhookDeadLetterMessage(
    string EventId,
    string PaymentId,
    string PartyId,
    string EndpointUrl,
    string PayloadJson,
    int DeliveryAttempts,
    string LastErrorMessage,
    DateTimeOffset FailedAt);
