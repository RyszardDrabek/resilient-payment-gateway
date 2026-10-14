using Microsoft.EntityFrameworkCore;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Ports;
using PaymentGateway.Infrastructure.Persistence;

namespace PaymentGateway.Infrastructure.Repositories;

public sealed class MerchantApiKeyRepository(PaymentDbContext dbContext) : IMerchantApiKeyRepository
{
    public async Task AddAsync(MerchantApiKey apiKey, CancellationToken ct = default)
    {
        dbContext.MerchantApiKeys.Add(apiKey);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<MerchantApiKey>> ListByMerchantAsync(string merchantId, CancellationToken ct = default)
    {
        return await dbContext.MerchantApiKeys
            .Where(k => k.MerchantId == merchantId)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<MerchantApiKey?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await dbContext.MerchantApiKeys
            .FirstOrDefaultAsync(k => k.Id == id, ct);
    }

    public async Task<MerchantApiKey?> FindByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        return await dbContext.MerchantApiKeys
            .FirstOrDefaultAsync(k => k.Prefix == prefix, ct);
    }

    public async Task UpdateAsync(MerchantApiKey apiKey, CancellationToken ct = default)
    {
        dbContext.MerchantApiKeys.Update(apiKey);
        await dbContext.SaveChangesAsync(ct);
    }
}
