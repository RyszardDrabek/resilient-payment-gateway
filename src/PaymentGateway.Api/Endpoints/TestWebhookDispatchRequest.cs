namespace PaymentGateway.Api.Endpoints;

public sealed record TestWebhookDispatchRequest(
    string? EndpointUrl = null,
    string? EventType = null);
