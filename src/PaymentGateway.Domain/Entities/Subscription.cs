using PaymentGateway.Domain.Exceptions;

namespace PaymentGateway.Domain.Entities;

public sealed class Subscription
{
    public string Id { get; private set; } = string.Empty;
    public string PartyId { get; private set; } = string.Empty;
    public string PaymentMethodId { get; private set; } = string.Empty;
    public long Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public SubscriptionInterval Interval { get; private set; }
    public SubscriptionState State { get; private set; } = SubscriptionState.Active;
    public DateTimeOffset NextBillingAt { get; private set; }
    public int FailureCount { get; private set; }
    public DateTimeOffset? LastBilledAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public int Version { get; private set; } = 1;

    private Subscription() { } // EF Core

    public Subscription(
        string id,
        string partyId,
        string paymentMethodId,
        long amount,
        string currency,
        SubscriptionInterval interval,
        DateTimeOffset? startDate = null,
        DateTimeOffset? createdAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(partyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(paymentMethodId);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);

        var trimmedCurrency = currency.Trim().ToUpperInvariant();
        if (trimmedCurrency.Length != 3 && !Payment.IsSupportedCryptoAsset(trimmedCurrency))
        {
            throw new ArgumentException("Currency must be a 3-letter ISO code or supported crypto asset.", nameof(currency));
        }

        Id = id;
        PartyId = partyId;
        PaymentMethodId = paymentMethodId;
        Amount = amount;
        Currency = trimmedCurrency;
        Interval = interval;
        State = SubscriptionState.Active;
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow;
        NextBillingAt = startDate ?? CalculateNextBilling(CreatedAt, interval);
        FailureCount = 0;
        Version = 1;
    }

    public void Pause()
    {
        if (State == SubscriptionState.Cancelled)
        {
            throw new InvalidOperationException("Cannot pause a cancelled subscription.");
        }

        if (State == SubscriptionState.Paused)
        {
            return;
        }

        State = SubscriptionState.Paused;
    }

    public void Resume(DateTimeOffset? now = null)
    {
        if (State == SubscriptionState.Cancelled)
        {
            throw new InvalidOperationException("Cannot resume a cancelled subscription.");
        }

        if (State != SubscriptionState.Paused)
        {
            throw new InvalidOperationException($"Cannot resume subscription in state '{State}'.");
        }

        var current = now ?? DateTimeOffset.UtcNow;
        State = SubscriptionState.Active;

        if (NextBillingAt < current)
        {
            NextBillingAt = current;
        }
    }

    public void Cancel(DateTimeOffset? cancelledAt = null)
    {
        if (State == SubscriptionState.Cancelled)
        {
            return;
        }

        State = SubscriptionState.Cancelled;
        CancelledAt = cancelledAt ?? DateTimeOffset.UtcNow;
    }

    public void RecordBillingSuccess(DateTimeOffset billedAt)
    {
        if (State == SubscriptionState.Cancelled)
        {
            throw new InvalidOperationException("Cannot record billing on a cancelled subscription.");
        }

        FailureCount = 0;
        LastBilledAt = billedAt;
        State = SubscriptionState.Active;
        NextBillingAt = CalculateNextBilling(billedAt, Interval);
    }

    public void RecordBillingFailure(int maxRetries = 3)
    {
        if (State == SubscriptionState.Cancelled)
        {
            throw new InvalidOperationException("Cannot record billing failure on a cancelled subscription.");
        }

        FailureCount++;
        if (FailureCount >= maxRetries)
        {
            State = SubscriptionState.PastDue;
        }
        else
        {
            // Transient dunning retry after 1 day
            NextBillingAt = NextBillingAt.AddDays(1);
        }
    }

    public static DateTimeOffset CalculateNextBilling(DateTimeOffset from, SubscriptionInterval interval) =>
        interval switch
        {
            SubscriptionInterval.Daily => from.AddDays(1),
            SubscriptionInterval.Weekly => from.AddDays(7),
            SubscriptionInterval.Monthly => from.AddMonths(1),
            SubscriptionInterval.Yearly => from.AddYears(1),
            _ => throw new ArgumentOutOfRangeException(nameof(interval), interval, "Unsupported interval")
        };
}
