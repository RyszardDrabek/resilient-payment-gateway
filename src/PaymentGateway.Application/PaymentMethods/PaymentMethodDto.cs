namespace PaymentGateway.Application.PaymentMethods;

public record PaymentMethodDto(
    string Id,
    string PartyId,
    string SettlementChannel,
    string CardBrand,
    string MaskedPan,
    int ExpiryMonth,
    int ExpiryYear,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt);
