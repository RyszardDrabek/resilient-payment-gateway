namespace PaymentGateway.Application.Webhooks;

public sealed record ReplayMerchantWebhookResult(
    bool Success,
    string Message,
    int? StatusCode = null,
    DateTimeOffset? ReplayedAt = null);
