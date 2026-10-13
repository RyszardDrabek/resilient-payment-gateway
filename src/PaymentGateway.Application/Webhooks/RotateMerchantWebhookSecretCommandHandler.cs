using System.Security.Cryptography;
using MediatR;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Application.Webhooks;

public sealed class RotateMerchantWebhookSecretCommandHandler(
    IMerchantWebhookSubscriptionRepository repository)
    : IRequestHandler<RotateMerchantWebhookSecretCommand, MerchantWebhookSubscriptionDto>
{
    public async Task<MerchantWebhookSubscriptionDto> Handle(
        RotateMerchantWebhookSecretCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PartyId);

        var existing = await repository.GetByPartyIdAsync(request.PartyId, cancellationToken);
        var newSecretKey = "whsec_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

        if (existing is null)
        {
            var newSubscription = new MerchantWebhookSubscription(
                Guid.NewGuid(),
                request.PartyId,
                "https://example.com/webhook",
                newSecretKey,
                isActive: false);

            await repository.AddAsync(newSubscription, cancellationToken);
            return new MerchantWebhookSubscriptionDto(
                newSubscription.Id,
                newSubscription.PartyId,
                newSubscription.EndpointUrl,
                newSubscription.SecretKey,
                newSubscription.IsActive);
        }

        existing.RotateSecretKey(newSecretKey);
        await repository.UpsertAsync(existing, cancellationToken);

        return new MerchantWebhookSubscriptionDto(
            existing.Id,
            existing.PartyId,
            existing.EndpointUrl,
            existing.SecretKey,
            existing.IsActive);
    }
}
