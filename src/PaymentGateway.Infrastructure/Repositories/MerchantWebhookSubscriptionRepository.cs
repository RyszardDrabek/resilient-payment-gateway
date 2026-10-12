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

    public async Task AddAsync(MerchantWebhookSubscription subscription, CancellationToken ct = default)
    {
        dbContext.MerchantWebhookSubscriptions.Add(subscription);
        await dbContext.SaveChangesAsync(ct);
    }
}
