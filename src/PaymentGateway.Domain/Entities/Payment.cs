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
    public ThreeDsActionDetails? ThreeDsAction { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public int Version { get; private set; } = 1;
    public long CapturedAmount { get; private set; }
    public long RefundedAmount { get; private set; }
    public long RemainingAuthorizedAmount => Math.Max(0L, Amount - CapturedAmount);
    public long RemainingRefundableAmount => Math.Max(0L, (CapturedAmount > 0 ? CapturedAmount : (State == PaymentState.Captured ? Amount : 0L)) - RefundedAmount);

    private readonly List<Events.IDomainEvent> _domainEvents = [];
    public IReadOnlyCollection<Events.IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void ClearDomainEvents() => _domainEvents.Clear();

    private static readonly HashSet<string> SupportedCryptoAssets = new(StringComparer.OrdinalIgnoreCase)
    {
        "USDC",
        "USDT"
    };

    public static bool IsSupportedCryptoAsset(string asset) =>
        SupportedCryptoAssets.Contains(asset);

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
        int version = 1,
        long capturedAmount = 0,
        long refundedAmount = 0,
        ThreeDsActionDetails? threeDsAction = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(partyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        ArgumentException.ThrowIfNullOrWhiteSpace(settlementChannel);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);

        var trimmedCurrency = currency.Trim();
        if (trimmedCurrency.Length != 3 && !IsSupportedCryptoAsset(trimmedCurrency))
        {
            throw new ArgumentException("Currency must be a 3-letter ISO code or supported crypto asset.", nameof(currency));
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
        CapturedAmount = Math.Max(0L, capturedAmount);
        RefundedAmount = Math.Max(0L, refundedAmount);
        ThreeDsAction = threeDsAction;
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

    public static Payment CreateRequiresAction(
        string partyId,
        long amount,
        string currency,
        string settlementChannel,
        string channelReference,
        ThreeDsActionDetails actionDetails)
    {
        var id = $"pay_{Guid.NewGuid():N}";
        var payment = new Payment(id, partyId, amount, currency, settlementChannel, PaymentState.RequiresAction, channelReference, threeDsAction: actionDetails);
        payment._domainEvents.Add(new Events.PaymentTransitionDomainEvent(
            payment.Id,
            payment.PartyId,
            Enums.PaymentLifecycleOutcome.RequiresAction,
            payment.Amount,
            payment.Currency,
            payment.Version,
            DateTimeOffset.UtcNow));
        return payment;
    }

    public void CompleteThreeDs(bool approved, string channelReference, string? declineReason = null)
    {
        if (State != PaymentState.RequiresAction)
        {
            throw new PaymentInvalidStateException(State, "CompleteThreeDs");
        }

        if (ThreeDsAction?.ExpiresAt != null && DateTimeOffset.UtcNow > ThreeDsAction.ExpiresAt)
        {
            State = PaymentState.Declined;
            DeclineReason = "ThreeDsExpired";
            ThreeDsAction = null;
            Version++;
            _domainEvents.Add(new Events.PaymentTransitionDomainEvent(
                Id,
                PartyId,
                Enums.PaymentLifecycleOutcome.Declined,
                Amount,
                Currency,
                Version,
                DateTimeOffset.UtcNow));
            return;
        }

        if (approved)
        {
            State = PaymentState.Authorized;
            ChannelReference = channelReference;
            ThreeDsAction = null;
            Version++;
            _domainEvents.Add(new Events.PaymentTransitionDomainEvent(
                Id,
                PartyId,
                Enums.PaymentLifecycleOutcome.Authorized,
                Amount,
                Currency,
                Version,
                DateTimeOffset.UtcNow));
        }
        else
        {
            State = PaymentState.Declined;
            DeclineReason = declineReason ?? "ThreeDsFailed";
            ChannelReference = channelReference;
            ThreeDsAction = null;
            Version++;
            _domainEvents.Add(new Events.PaymentTransitionDomainEvent(
                Id,
                PartyId,
                Enums.PaymentLifecycleOutcome.Declined,
                Amount,
                Currency,
                Version,
                DateTimeOffset.UtcNow));
        }
    }

    public void Capture(string channelReference, long? amount = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelReference);

        if (State != PaymentState.Authorized && State != PaymentState.PartiallyCaptured)
        {
            throw new PaymentInvalidStateException(State, "Capture");
        }

        var effectiveAmount = amount ?? RemainingAuthorizedAmount;
        if (effectiveAmount <= 0)
        {
            throw new PaymentValidationException("Capture amount must be greater than zero.");
        }

        if (effectiveAmount > RemainingAuthorizedAmount)
        {
            throw new PaymentValidationException($"Capture amount {effectiveAmount} exceeds remaining authorized amount {RemainingAuthorizedAmount}.");
        }

        CapturedAmount += effectiveAmount;
        State = CapturedAmount == Amount ? PaymentState.Captured : PaymentState.PartiallyCaptured;
        ChannelReference = channelReference;
        Version++;

        var outcome = State == PaymentState.Captured
            ? Enums.PaymentLifecycleOutcome.Captured
            : Enums.PaymentLifecycleOutcome.PartiallyCaptured;

        _domainEvents.Add(new Events.PaymentTransitionDomainEvent(
            Id,
            PartyId,
            outcome,
            effectiveAmount,
            Currency,
            Version,
            DateTimeOffset.UtcNow));
    }

    public void Cancel(string channelReference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelReference);

        // AC-2 & AC-4: Cancel when authorized or partially captured (voids remaining uncaptured authorization)
        if (State != PaymentState.Authorized && State != PaymentState.PartiallyCaptured)
        {
            throw new PaymentInvalidStateException(State, "Cancel");
        }

        var uncapturedBalance = RemainingAuthorizedAmount;
        State = PaymentState.Cancelled;
        ChannelReference = channelReference;
        Version++;

        _domainEvents.Add(new Events.PaymentTransitionDomainEvent(
            Id,
            PartyId,
            Enums.PaymentLifecycleOutcome.Cancelled,
            uncapturedBalance,
            Currency,
            Version,
            DateTimeOffset.UtcNow));
    }

    public void Refund(string channelReference, long? amount = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelReference);

        if (State == PaymentState.Captured && CapturedAmount == 0)
        {
            CapturedAmount = Amount;
        }

        var isRemainderCancelled = State == PaymentState.Cancelled && CapturedAmount > 0;
        if (State != PaymentState.Captured && State != PaymentState.PartiallyRefunded && !isRemainderCancelled)
        {
            throw new PaymentInvalidStateException(State, "Refund");
        }

        var availableRefundable = (CapturedAmount > 0 ? CapturedAmount : Amount) - RefundedAmount;
        if (availableRefundable <= 0)
        {
            throw new PaymentInvalidStateException(State, "Refund");
        }

        var effectiveAmount = amount ?? availableRefundable;
        if (effectiveAmount <= 0)
        {
            throw new PaymentValidationException("Refund amount must be greater than zero.");
        }

        if (effectiveAmount > availableRefundable)
        {
            throw new PaymentValidationException($"Refund amount {effectiveAmount} exceeds remaining refundable balance {availableRefundable}.");
        }

        RefundedAmount += effectiveAmount;
        var isFullyRefunded = RefundedAmount == (CapturedAmount > 0 ? CapturedAmount : Amount);
        State = isFullyRefunded ? PaymentState.Refunded : PaymentState.PartiallyRefunded;
        ChannelReference = channelReference;
        Version++;

        var outcome = isFullyRefunded ? Enums.PaymentLifecycleOutcome.Refunded : Enums.PaymentLifecycleOutcome.PartiallyRefunded;

        _domainEvents.Add(new Events.PaymentTransitionDomainEvent(
            Id,
            PartyId,
            outcome,
            effectiveAmount,
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
            Enums.PaymentLifecycleOutcome.PartiallyCaptured => PaymentState.PartiallyCaptured,
            Enums.PaymentLifecycleOutcome.Cancelled => PaymentState.Cancelled,
            Enums.PaymentLifecycleOutcome.Refunded => PaymentState.Refunded,
            Enums.PaymentLifecycleOutcome.PartiallyRefunded => PaymentState.PartiallyRefunded,
            Enums.PaymentLifecycleOutcome.Pending => PaymentState.Pending,
            Enums.PaymentLifecycleOutcome.Unknown => PaymentState.Unknown,
            Enums.PaymentLifecycleOutcome.RequiresAction => PaymentState.RequiresAction,
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
        if (State is PaymentState.Refunded or PaymentState.Declined)
        {
            return false;
        }

        // Cancelled cannot revert unless remainder-cancelled with captured balance being refunded
        if (State == PaymentState.Cancelled)
        {
            if (CapturedAmount > 0 && targetState.Value is PaymentState.PartiallyRefunded or PaymentState.Refunded)
            {
                // Allowed
            }
            else
            {
                return false;
            }
        }

        // PartiallyRefunded cannot revert to Captured, PartiallyCaptured, Authorized, Declined, Pending, or Unknown
        if (State == PaymentState.PartiallyRefunded
            && (targetState.Value == PaymentState.Captured
                || targetState.Value == PaymentState.PartiallyCaptured
                || targetState.Value == PaymentState.Authorized
                || targetState.Value == PaymentState.Declined
                || targetState.Value == PaymentState.Pending
                || targetState.Value == PaymentState.Unknown))
        {
            return false;
        }

        // Captured cannot revert to Authorized, PartiallyCaptured, Declined, Pending, or Unknown
        if (State == PaymentState.Captured
            && (targetState.Value == PaymentState.Authorized
                || targetState.Value == PaymentState.PartiallyCaptured
                || targetState.Value == PaymentState.Declined
                || targetState.Value == PaymentState.Pending
                || targetState.Value == PaymentState.Unknown))
        {
            return false;
        }

        // PartiallyCaptured cannot revert to Authorized or Declined
        if (State == PaymentState.PartiallyCaptured
            && (targetState.Value == PaymentState.Authorized
                || targetState.Value == PaymentState.Declined))
        {
            return false;
        }

        // Authorized cannot revert to Declined
        if (State == PaymentState.Authorized && targetState.Value == PaymentState.Declined)
        {
            return false;
        }

        // RequiresAction cannot revert to Pending or Unknown
        if (State == PaymentState.RequiresAction
            && targetState.Value is not (PaymentState.Authorized or PaymentState.Declined))
        {
            return false;
        }

        // Valid transition applied
        State = targetState.Value;
        if (targetState.Value == PaymentState.Captured && CapturedAmount == 0)
        {
            CapturedAmount = Amount;
        }
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

