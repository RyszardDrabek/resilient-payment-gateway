using PaymentGateway.Application.Fx.Models;
using PaymentGateway.Domain.Entities;

namespace PaymentGateway.Application.Fx;

public static class FxQuoteMapper
{
    public static FxQuoteDto ToDto(CurrencyQuote quote, DateTimeOffset? now = null)
    {
        var currentTime = now ?? DateTimeOffset.UtcNow;
        var remainingSeconds = (int)Math.Max(0, (quote.ExpiresAt - currentTime).TotalSeconds);

        return new FxQuoteDto(
            QuoteId: quote.Id,
            PartyId: quote.PartyId,
            BaseCurrency: quote.BaseCurrency,
            BaseAmount: quote.BaseAmount,
            TargetCurrency: quote.TargetCurrency,
            TargetAmount: quote.TargetAmount,
            SpotRate: quote.SpotRate,
            SpreadBps: quote.SpreadBps,
            ExchangeRate: quote.ExchangeRate,
            CreatedAt: quote.CreatedAt,
            ExpiresAt: quote.ExpiresAt,
            Status: quote.Status.ToString(),
            RemainingSeconds: remainingSeconds);
    }
}
