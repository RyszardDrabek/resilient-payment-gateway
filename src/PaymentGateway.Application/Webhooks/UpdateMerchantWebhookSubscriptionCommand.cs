using MediatR;

namespace PaymentGateway.Application.Webhooks;

public sealed record UpdateMerchantWebhookSubscriptionCommand(
    string PartyId,
    string EndpointUrl,
    bool IsActive) : IRequest<MerchantWebhookSubscriptionDto>;
