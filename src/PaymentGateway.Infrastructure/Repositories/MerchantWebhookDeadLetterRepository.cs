using Microsoft.EntityFrameworkCore;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Ports;
using PaymentGateway.Infrastructure.Persistence;

namespace PaymentGateway.Infrastructure.Repositories;

public sealed class MerchantWebhookDeadLetterRepository(PaymentDbContext dbContext)
    : IMerchantWebhookDeadLetterRepository
{
    public async Task AddAsync(MerchantWebhookDeadLetter deadLetter, CancellationToken ct = default)
    {
        dbContext.MerchantWebhookDeadLetters.Add(deadLetter);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<MerchantWebhookDeadLetter?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await dbContext.MerchantWebhookDeadLetters
            .FirstOrDefaultAsync(d => d.Id == id, ct);
    }

    public async Task<IReadOnlyList<MerchantWebhookDeadLetter>> ListByMerchantAsync(
        string merchantId,
        string? status = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default)
    {
        var query = dbContext.MerchantWebhookDeadLetters
            .AsNoTracking()
            .Where(d => d.MerchantId == merchantId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(d => d.Status == status);
        }

        var pageNumber = Math.Max(1, page);
        var size = Math.Clamp(pageSize, 1, 100);

        var results = await query
            .OrderByDescending(d => d.FailedAt)
            .Skip((pageNumber - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        return results;
    }

    public async Task UpdateAsync(MerchantWebhookDeadLetter deadLetter, CancellationToken ct = default)
    {
        dbContext.MerchantWebhookDeadLetters.Update(deadLetter);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<int> CleanupExpiredAsync(DateTimeOffset cutoff, CancellationToken ct = default)
    {
        return await dbContext.MerchantWebhookDeadLetters
            .Where(d => d.FailedAt < cutoff)
            .ExecuteDeleteAsync(ct);
    }
}
