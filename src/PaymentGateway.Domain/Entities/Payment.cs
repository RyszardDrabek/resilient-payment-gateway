namespace PaymentGateway.Domain.Entities;

public sealed class Payment
{
    public string Id { get; private set; } = string.Empty;
    public string PartyId { get; private set; } = string.Empty;
    public long Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public string SettlementChannel { get; private set; } = string.Empty;
    public string? ChannelReference { get; private set; }
    public string? DeclineReason { get; private set; }
    public PaymentState State { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Payment() { } // EF Core

    public Payment(
        string id,
        string partyId,
        long amount,
        string currency,
        string settlementChannel,
        PaymentState state,
        string? channelReference = null,
        string? declineReason = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(partyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        ArgumentException.ThrowIfNullOrWhiteSpace(settlementChannel);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);

        if (currency.Trim().Length != 3)
        {
            throw new ArgumentException("Currency must be a 3-letter ISO code.", nameof(currency));
        }

        Id = id;
        PartyId = partyId;
        Amount = amount;
        Currency = currency;
        SettlementChannel = settlementChannel;
        State = state;
        ChannelReference = channelReference;
        DeclineReason = declineReason;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public static Payment Authorize(
        string partyId,
        long amount,
        string currency,
        string settlementChannel,
        string channelReference)
    {
        var id = $"pay_{Guid.NewGuid():N}";
        return new Payment(id, partyId, amount, currency, settlementChannel, PaymentState.Authorized, channelReference);
    }

    public static Payment Decline(
        string partyId,
        long amount,
        string currency,
        string settlementChannel,
        string? channelReference = null,
        string? declineReason = null)
    {
        var id = $"pay_{Guid.NewGuid():N}";
        return new Payment(id, partyId, amount, currency, settlementChannel, PaymentState.Declined, channelReference, declineReason);
    }
}
