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
        if (existing is null)
        {
            throw new InvalidOperationException(
                $"Cannot rotate webhook secret key for party '{request.PartyId}' because no subscription exists. Please configure an endpoint URL first.");
        }

        var newSecretKey = "whsec_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
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
