using Microsoft.EntityFrameworkCore;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Ports;
using PaymentGateway.Infrastructure.Persistence;

namespace PaymentGateway.Infrastructure.Repositories;

public sealed class PayoutRepository(PaymentDbContext dbContext) : IPayoutRepository
{
    public async Task<MerchantPayoutBatch?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        return await dbContext.PayoutBatches
            .Include(b => b.Items)
            .FirstOrDefaultAsync(b => b.Id == id, ct);
    }

    public async Task<IReadOnlyList<MerchantPayoutBatch>> ListByMerchantIdAsync(
        string merchantId,
        int skip = 0,
        int take = 50,
        CancellationToken ct = default)
    {
        return await dbContext.PayoutBatches
            .Include(b => b.Items)
            .Where(b => b.MerchantId == merchantId)
            .OrderByDescending(b => b.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<HashSet<string>> GetPaidOutPaymentIdsAsync(IEnumerable<string> paymentIds, CancellationToken ct = default)
    {
        var idList = paymentIds.Distinct().ToList();
        if (idList.Count == 0)
        {
            return [];
        }

        var paidOut = await dbContext.PayoutItems
            .Where(pi => idList.Contains(pi.PaymentId))
            .Select(pi => pi.PaymentId)
            .Distinct()
            .ToListAsync(ct);

        return paidOut.ToHashSet();
    }

    public async Task SaveAsync(MerchantPayoutBatch batch, CancellationToken ct = default)
    {
        var exists = await dbContext.PayoutBatches.AnyAsync(b => b.Id == batch.Id, ct);
        if (!exists)
        {
            dbContext.PayoutBatches.Add(batch);
        }
        else
        {
            dbContext.PayoutBatches.Update(batch);
        }

        await dbContext.SaveChangesAsync(ct);
    }
}
