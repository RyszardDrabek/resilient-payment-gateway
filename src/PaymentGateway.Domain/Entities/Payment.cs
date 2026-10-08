using PaymentGateway.Domain.Exceptions;

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
    public int Version { get; private set; } = 1;

    private readonly List<Events.IDomainEvent> _domainEvents = [];
    public IReadOnlyCollection<Events.IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void ClearDomainEvents() => _domainEvents.Clear();

    private Payment() { } // EF Core

    public Payment(
        string id,
        string partyId,
        long amount,
        string currency,
        string settlementChannel,
        PaymentState state,
        string? channelReference = null,
        string? declineReason = null,
        int version = 1)
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
        Version = version > 0 ? version : 1;
    }

    public static Payment Authorize(
        string partyId,
        long amount,
        string currency,
        string settlementChannel,
        string channelReference)
    {
        var id = $"pay_{Guid.NewGuid():N}";
        var payment = new Payment(id, partyId, amount, currency, settlementChannel, PaymentState.Authorized, channelReference);
        payment._domainEvents.Add(new Events.PaymentTransitionDomainEvent(
            payment.Id,
            payment.PartyId,
            Enums.PaymentLifecycleOutcome.Authorized,
            payment.Amount,
            payment.Currency,
            payment.Version,
            DateTimeOffset.UtcNow));
        return payment;
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
        var payment = new Payment(id, partyId, amount, currency, settlementChannel, PaymentState.Declined, channelReference, declineReason);
        payment._domainEvents.Add(new Events.PaymentTransitionDomainEvent(
            payment.Id,
            payment.PartyId,
            Enums.PaymentLifecycleOutcome.Declined,
            payment.Amount,
            payment.Currency,
            payment.Version,
            DateTimeOffset.UtcNow));
        return payment;
    }

    public static Payment CreatePending(
        string partyId,
        long amount,
        string currency,
        string settlementChannel,
        string? channelReference = null,
        string? reason = null)
    {
        var id = $"pay_{Guid.NewGuid():N}";
        var payment = new Payment(id, partyId, amount, currency, settlementChannel, PaymentState.Pending, channelReference, reason);
        payment._domainEvents.Add(new Events.PaymentTransitionDomainEvent(
            payment.Id,
            payment.PartyId,
            Enums.PaymentLifecycleOutcome.Pending,
            payment.Amount,
            payment.Currency,
            payment.Version,
            DateTimeOffset.UtcNow));
        return payment;
    }

    public static Payment CreateUnknown(
        string partyId,
        long amount,
        string currency,
        string settlementChannel,
        string? channelReference = null,
        string? reason = null)
    {
        var id = $"pay_{Guid.NewGuid():N}";
        var payment = new Payment(id, partyId, amount, currency, settlementChannel, PaymentState.Unknown, channelReference, reason);
        payment._domainEvents.Add(new Events.PaymentTransitionDomainEvent(
            payment.Id,
            payment.PartyId,
            Enums.PaymentLifecycleOutcome.Unknown,
            payment.Amount,
            payment.Currency,
            payment.Version,
            DateTimeOffset.UtcNow));
        return payment;
    }

    public void Capture(string channelReference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelReference);

        if (State != PaymentState.Authorized)
        {
            throw new PaymentInvalidStateException(State, "Capture");
        }

        State = PaymentState.Captured;
        ChannelReference = channelReference;
        Version++;

        _domainEvents.Add(new Events.PaymentTransitionDomainEvent(
            Id,
            PartyId,
            Enums.PaymentLifecycleOutcome.Captured,
            Amount,
            Currency,
            Version,
            DateTimeOffset.UtcNow));
    }

    public void Cancel(string channelReference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelReference);

        // AC-2: Cancel when authorized and uncaptured
        if (State != PaymentState.Authorized)
        {
            throw new PaymentInvalidStateException(State, "Cancel");
        }

        State = PaymentState.Cancelled;
        ChannelReference = channelReference;
        Version++;

        _domainEvents.Add(new Events.PaymentTransitionDomainEvent(
            Id,
            PartyId,
            Enums.PaymentLifecycleOutcome.Cancelled,
            Amount,
            Currency,
            Version,
            DateTimeOffset.UtcNow));
    }

    public void Refund(string channelReference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelReference);

        // AC-3: Refund a captured payment
        if (State != PaymentState.Captured)
        {
            throw new PaymentInvalidStateException(State, "Refund");
        }

        State = PaymentState.Refunded;
        ChannelReference = channelReference;
        Version++;

        _domainEvents.Add(new Events.PaymentTransitionDomainEvent(
            Id,
            PartyId,
            Enums.PaymentLifecycleOutcome.Refunded,
            Amount,
            Currency,
            Version,
            DateTimeOffset.UtcNow));
    }

    public bool ApplyAcquirerOutcome(
        Enums.PaymentLifecycleOutcome outcome,
        string? channelReference = null,
        string? reason = null)
    {
        var targetState = outcome switch
        {
            Enums.PaymentLifecycleOutcome.Authorized => PaymentState.Authorized,
            Enums.PaymentLifecycleOutcome.Declined => PaymentState.Declined,
            Enums.PaymentLifecycleOutcome.Captured => PaymentState.Captured,
            Enums.PaymentLifecycleOutcome.Cancelled => PaymentState.Cancelled,
            Enums.PaymentLifecycleOutcome.Refunded => PaymentState.Refunded,
            _ => (PaymentState?)null
        };

        if (targetState is null)
        {
            return false;
        }

        // AC-1: Duplicate absorption (no double-apply, no version increment, no duplicate event)
        if (State == targetState.Value)
        {
            return false;
        }

        // AC-1: Out-of-order protection — acquirer truth progression:
        // Terminal states cannot be reverted:
        if (State is PaymentState.Refunded or PaymentState.Cancelled or PaymentState.Declined)
        {
            return false;
        }

        // Captured cannot revert to Authorized or Declined
        if (State == PaymentState.Captured && targetState.Value is PaymentState.Authorized or PaymentState.Declined)
        {
            return false;
        }

        // Valid transition applied
        State = targetState.Value;
        if (!string.IsNullOrWhiteSpace(channelReference))
        {
            ChannelReference = channelReference;
        }

        if (targetState.Value == PaymentState.Declined && !string.IsNullOrWhiteSpace(reason))
        {
            DeclineReason = reason;
        }

        Version++;

        _domainEvents.Add(new Events.PaymentTransitionDomainEvent(
            Id,
            PartyId,
            outcome,
            Amount,
            Currency,
            Version,
            DateTimeOffset.UtcNow));

        return true;
    }
}

