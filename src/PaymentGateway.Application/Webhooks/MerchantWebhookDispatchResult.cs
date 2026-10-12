namespace PaymentGateway.Application.Webhooks;

public sealed record MerchantWebhookDispatchResult(
    bool Success,
    string EndpointUrl,
    string PayloadJson,
    int DeliveryAttempts,
    string? ErrorMessage = null);
