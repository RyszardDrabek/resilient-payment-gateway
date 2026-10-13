using MediatR;

namespace PaymentGateway.Application.Webhooks;

public sealed record GetMerchantWebhookSubscriptionQuery(string PartyId)
    : IRequest<MerchantWebhookSubscriptionDto?>;
