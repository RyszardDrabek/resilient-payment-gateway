using PaymentGateway.Domain.Entities;

namespace PaymentGateway.Domain.Ports;

public interface IFxQuoteRepository
{
    Task<CurrencyQuote?> GetByIdAsync(string quoteId, CancellationToken ct = default);
    Task AddAsync(CurrencyQuote quote, CancellationToken ct = default);
    Task UpdateAsync(CurrencyQuote quote, CancellationToken ct = default);

    Task<int> ExpireStaleQuotesAsync(DateTimeOffset now, int batchSize = 1000, CancellationToken ct = default) => Task.FromResult(0);
}
