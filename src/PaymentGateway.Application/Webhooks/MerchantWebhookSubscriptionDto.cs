namespace PaymentGateway.Application.Webhooks;

public sealed record MerchantWebhookSubscriptionDto(
    Guid Id,
    string PartyId,
    string EndpointUrl,
    string SecretKey,
    bool IsActive);
