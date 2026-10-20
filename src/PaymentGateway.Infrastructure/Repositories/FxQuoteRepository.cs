using Microsoft.EntityFrameworkCore;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Ports;
using PaymentGateway.Infrastructure.Persistence;

namespace PaymentGateway.Infrastructure.Repositories;

public sealed class FxQuoteRepository(PaymentDbContext dbContext) : IFxQuoteRepository
{
    public async Task<CurrencyQuote?> GetByIdAsync(string quoteId, CancellationToken ct = default)
    {
        return await dbContext.CurrencyQuotes.FirstOrDefaultAsync(q => q.Id == quoteId, ct);
    }

    public async Task AddAsync(CurrencyQuote quote, CancellationToken ct = default)
    {
        await dbContext.CurrencyQuotes.AddAsync(quote, ct);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(CurrencyQuote quote, CancellationToken ct = default)
    {
        dbContext.CurrencyQuotes.Update(quote);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<int> ExpireStaleQuotesAsync(DateTimeOffset now, int batchSize = 1000, CancellationToken ct = default)
    {
        var staleIds = await dbContext.CurrencyQuotes
            .Where(q => q.Status == PaymentGateway.Domain.Enums.CurrencyQuoteStatus.Active && q.ExpiresAt <= now)
            .OrderBy(q => q.ExpiresAt)
            .Select(q => q.Id)
            .Take(batchSize)
            .ToListAsync(ct);

        if (staleIds.Count == 0)
        {
            return 0;
        }

        return await dbContext.CurrencyQuotes
            .Where(q => staleIds.Contains(q.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(q => q.Status, PaymentGateway.Domain.Enums.CurrencyQuoteStatus.Expired), ct);
    }
}
