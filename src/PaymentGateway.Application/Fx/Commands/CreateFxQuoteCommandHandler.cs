using MediatR;
using PaymentGateway.Application.Fx.Models;
using PaymentGateway.Application.Fx.Ports;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Application.Fx.Commands;

public sealed class CreateFxQuoteCommandHandler(
    IFxRateProvider rateProvider,
    IFxQuoteRepository quoteRepository,
    FxOptions? options = null) : IRequestHandler<CreateFxQuoteCommand, FxQuoteDto>
{
    private readonly FxOptions _options = options ?? new FxOptions();

    public async Task<FxQuoteDto> Handle(CreateFxQuoteCommand request, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BaseCurrency);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetCurrency);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.BaseAmount);

        var baseCurrency = request.BaseCurrency.Trim().ToUpperInvariant();
        var targetCurrency = request.TargetCurrency.Trim().ToUpperInvariant();

        decimal spotRate;
        int spreadBps;

        if (string.Equals(baseCurrency, targetCurrency, StringComparison.OrdinalIgnoreCase))
        {
            spotRate = 1.0m;
            spreadBps = 0;
        }
        else
        {
            spotRate = await rateProvider.GetSpotRateAsync(baseCurrency, targetCurrency, ct);
            spreadBps = _options.DefaultSpreadBps;
        }

        var ttlMinutes = request.TtlMinutes.HasValue && request.TtlMinutes.Value > 0
            ? Math.Min(request.TtlMinutes.Value, _options.MaxTtlMinutes)
            : _options.DefaultTtlMinutes;

        var now = DateTimeOffset.UtcNow;
        var quoteId = $"fxq_{Guid.NewGuid():N}";

        var quote = CurrencyQuote.Create(
            id: quoteId,
            partyId: request.PartyId,
            baseCurrency: baseCurrency,
            baseAmount: request.BaseAmount,
            targetCurrency: targetCurrency,
            spotRate: spotRate,
            spreadBps: spreadBps,
            ttl: TimeSpan.FromMinutes(ttlMinutes),
            now: now);

        await quoteRepository.AddAsync(quote, ct);

        return FxQuoteMapper.ToDto(quote, now);
    }
}
