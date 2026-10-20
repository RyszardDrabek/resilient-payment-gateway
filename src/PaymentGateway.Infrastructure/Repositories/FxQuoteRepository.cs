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
}
