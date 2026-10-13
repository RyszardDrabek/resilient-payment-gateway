using MediatR;

namespace PaymentGateway.Application.Webhooks;

public sealed record RotateMerchantWebhookSecretCommand(string PartyId)
    : IRequest<MerchantWebhookSubscriptionDto>;
