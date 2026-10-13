using System.Security.Cryptography;
using MediatR;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Application.Webhooks;

public sealed class UpdateMerchantWebhookSubscriptionCommandHandler(
    IMerchantWebhookSubscriptionRepository repository)
    : IRequestHandler<UpdateMerchantWebhookSubscriptionCommand, MerchantWebhookSubscriptionDto>
{
    public async Task<MerchantWebhookSubscriptionDto> Handle(
        UpdateMerchantWebhookSubscriptionCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PartyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.EndpointUrl);

        var existing = await repository.GetByPartyIdAsync(request.PartyId, cancellationToken);
        if (existing is null)
        {
            var initialSecretKey = "whsec_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var newSubscription = new MerchantWebhookSubscription(
                Guid.NewGuid(),
                request.PartyId,
                request.EndpointUrl,
                initialSecretKey,
                request.IsActive);

            await repository.AddAsync(newSubscription, cancellationToken);
            return new MerchantWebhookSubscriptionDto(
                newSubscription.Id,
                newSubscription.PartyId,
                newSubscription.EndpointUrl,
                newSubscription.SecretKey,
                newSubscription.IsActive);
        }

        existing.UpdateEndpoint(request.EndpointUrl, request.IsActive);
        await repository.UpsertAsync(existing, cancellationToken);

        return new MerchantWebhookSubscriptionDto(
            existing.Id,
            existing.PartyId,
            existing.EndpointUrl,
            existing.SecretKey,
            existing.IsActive);
    }
}
