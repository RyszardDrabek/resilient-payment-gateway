namespace PaymentGateway.Api.Endpoints;

public sealed record UpdateWebhookSubscriptionRequest(
    string EndpointUrl,
    bool IsActive);
