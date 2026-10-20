using PaymentGateway.Domain.Entities;

namespace PaymentGateway.Domain.Ports;

public interface IFxQuoteRepository
{
    Task<CurrencyQuote?> GetByIdAsync(string quoteId, CancellationToken ct = default);
    Task AddAsync(CurrencyQuote quote, CancellationToken ct = default);
    Task UpdateAsync(CurrencyQuote quote, CancellationToken ct = default);
}
