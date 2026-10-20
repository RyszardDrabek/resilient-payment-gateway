namespace PaymentGateway.Application.Fx.Models;

public sealed record FxQuoteDto(
    string QuoteId,
    string? PartyId,
    string BaseCurrency,
    long BaseAmount,
    string TargetCurrency,
    long TargetAmount,
    decimal SpotRate,
    int SpreadBps,
    decimal ExchangeRate,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string Status,
    int RemainingSeconds);
