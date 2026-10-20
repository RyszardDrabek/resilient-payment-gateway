using MediatR;
using PaymentGateway.Application.Fx.Models;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Application.Fx.Queries;

public sealed class GetFxQuoteByIdQueryHandler(IFxQuoteRepository quoteRepository)
    : IRequestHandler<GetFxQuoteByIdQuery, FxQuoteDto?>
{
    public async Task<FxQuoteDto?> Handle(GetFxQuoteByIdQuery request, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.QuoteId);

        var quote = await quoteRepository.GetByIdAsync(request.QuoteId, ct);
        if (quote is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        if (quote.Status == Domain.Enums.CurrencyQuoteStatus.Active && now >= quote.ExpiresAt)
        {
            quote.Expire(now);
            await quoteRepository.UpdateAsync(quote, ct);
        }

        var remainingSeconds = (int)Math.Max(0, (quote.ExpiresAt - now).TotalSeconds);

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
