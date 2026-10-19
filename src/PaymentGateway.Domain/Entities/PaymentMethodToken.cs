namespace PaymentGateway.Domain.Entities;

public sealed class PaymentMethodToken
{
    public string Id { get; private set; } = string.Empty;
    public string PartyId { get; private set; } = string.Empty;
    public string SettlementChannel { get; private set; } = string.Empty;
    public string CardBrand { get; private set; } = string.Empty;
    public string MaskedPan { get; private set; } = string.Empty;
    public int ExpiryMonth { get; private set; }
    public int ExpiryYear { get; private set; }
    public string Token { get; private set; } = string.Empty;
    public PaymentMethodStatus Status { get; private set; } = PaymentMethodStatus.Active;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    private PaymentMethodToken() { } // EF Core

    public PaymentMethodToken(
        string id,
        string partyId,
        string settlementChannel,
        string cardBrand,
        string maskedPan,
        int expiryMonth,
        int expiryYear,
        string token,
        DateTimeOffset? createdAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(partyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(settlementChannel);
        ArgumentException.ThrowIfNullOrWhiteSpace(cardBrand);
        ArgumentException.ThrowIfNullOrWhiteSpace(maskedPan);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        if (expiryMonth is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(expiryMonth), "Expiry month must be between 1 and 12.");
        }

        if (expiryYear < 2000)
        {
            throw new ArgumentOutOfRangeException(nameof(expiryYear), "Expiry year must be 2000 or greater.");
        }

        Id = id;
        PartyId = partyId;
        SettlementChannel = settlementChannel;
        CardBrand = cardBrand;
        MaskedPan = maskedPan;
        ExpiryMonth = expiryMonth;
        ExpiryYear = expiryYear;
        Token = token;
        Status = PaymentMethodStatus.Active;
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow;
    }

    public void Revoke(DateTimeOffset? revokedAt = null)
    {
        if (Status == PaymentMethodStatus.Revoked)
        {
            return;
        }

        Status = PaymentMethodStatus.Revoked;
        RevokedAt = revokedAt ?? DateTimeOffset.UtcNow;
    }

    public bool IsExpired(DateTimeOffset? asOf = null)
    {
        var reference = asOf ?? DateTimeOffset.UtcNow;
        if (ExpiryYear < reference.Year)
        {
            return true;
        }

        return ExpiryYear == reference.Year && ExpiryMonth < reference.Month;
    }
}
