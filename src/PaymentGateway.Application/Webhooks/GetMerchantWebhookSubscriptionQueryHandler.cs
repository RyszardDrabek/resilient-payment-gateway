using MediatR;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Application.Webhooks;

public sealed class GetMerchantWebhookSubscriptionQueryHandler(
    IMerchantWebhookSubscriptionRepository repository)
    : IRequestHandler<GetMerchantWebhookSubscriptionQuery, MerchantWebhookSubscriptionDto?>
{
    public async Task<MerchantWebhookSubscriptionDto?> Handle(
        GetMerchantWebhookSubscriptionQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var subscription = await repository.GetByPartyIdAsync(request.PartyId, cancellationToken);
        if (subscription is null)
        {
            return null;
        }

        return new MerchantWebhookSubscriptionDto(
            subscription.Id,
            subscription.PartyId,
            subscription.EndpointUrl,
            subscription.SecretKey,
            subscription.IsActive);
    }
}
