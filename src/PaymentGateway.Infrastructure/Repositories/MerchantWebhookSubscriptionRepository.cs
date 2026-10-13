using Microsoft.EntityFrameworkCore;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Ports;
using PaymentGateway.Infrastructure.Persistence;

namespace PaymentGateway.Infrastructure.Repositories;

public sealed class MerchantWebhookSubscriptionRepository(PaymentDbContext dbContext)
    : IMerchantWebhookSubscriptionRepository
{
    public async Task<MerchantWebhookSubscription?> GetActiveByPartyIdAsync(string partyId, CancellationToken ct = default)
    {
        return await dbContext.MerchantWebhookSubscriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.PartyId == partyId && s.IsActive, ct);
    }

    public async Task<MerchantWebhookSubscription?> GetByPartyIdAsync(string partyId, CancellationToken ct = default)
    {
        return await dbContext.MerchantWebhookSubscriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.PartyId == partyId, ct);
    }

    public async Task AddAsync(MerchantWebhookSubscription subscription, CancellationToken ct = default)
    {
        dbContext.MerchantWebhookSubscriptions.Add(subscription);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task UpsertAsync(MerchantWebhookSubscription subscription, CancellationToken ct = default)
    {
        var existing = await dbContext.MerchantWebhookSubscriptions
            .FirstOrDefaultAsync(s => s.PartyId == subscription.PartyId, ct);

        if (existing is null)
        {
            dbContext.MerchantWebhookSubscriptions.Add(subscription);
        }
        else
        {
            existing.UpdateEndpoint(subscription.EndpointUrl, subscription.IsActive);
            existing.RotateSecretKey(subscription.SecretKey);
        }

        await dbContext.SaveChangesAsync(ct);
    }
}
