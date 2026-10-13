using MediatR;

namespace PaymentGateway.Application.Webhooks;

public sealed record TestMerchantWebhookDispatchCommand(
    string PartyId,
    string? EndpointUrl = null,
    string? EventType = null) : IRequest<TestMerchantWebhookDispatchResultDto>;
