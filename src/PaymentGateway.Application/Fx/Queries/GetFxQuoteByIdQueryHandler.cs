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

        return FxQuoteMapper.ToDto(quote, now);
    }
}
