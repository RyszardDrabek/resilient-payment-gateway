using Microsoft.EntityFrameworkCore;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Ports;
using PaymentGateway.Infrastructure.Persistence;

namespace PaymentGateway.Infrastructure.Repositories;

public sealed class SubscriptionRepository(PaymentDbContext dbContext) : ISubscriptionRepository
{
    public async Task<Subscription?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return await dbContext.Subscriptions
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Subscription>> ListByPartyIdAsync(string partyId, CancellationToken cancellationToken = default)
    {
        return await dbContext.Subscriptions
            .Where(s => s.PartyId == partyId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Subscription>> ListDueSubscriptionsAsync(
        DateTimeOffset asOf,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Subscriptions
            .Where(s => s.State == SubscriptionState.Active && s.NextBillingAt <= asOf)
            .OrderBy(s => s.NextBillingAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Subscription>> ListAsync(int skip = 0, int take = 50, CancellationToken cancellationToken = default)
    {
        return await dbContext.Subscriptions
            .OrderByDescending(s => s.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task SaveAsync(Subscription subscription, CancellationToken cancellationToken = default)
    {
        var exists = await dbContext.Subscriptions.AnyAsync(s => s.Id == subscription.Id, cancellationToken);
        if (!exists)
        {
            dbContext.Subscriptions.Add(subscription);
        }
        else
        {
            dbContext.Subscriptions.Update(subscription);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new PaymentGateway.Domain.Exceptions.PaymentConcurrencyException(subscription.Id);
        }
    }
}
