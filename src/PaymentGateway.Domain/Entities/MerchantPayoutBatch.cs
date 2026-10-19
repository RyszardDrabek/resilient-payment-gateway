namespace PaymentGateway.Domain.Entities;

public sealed class MerchantPayoutBatch
{
    public string Id { get; private set; } = string.Empty;
    public string MerchantId { get; private set; } = string.Empty;
    public DateTimeOffset PeriodStart { get; private set; }
    public DateTimeOffset PeriodEnd { get; private set; }
    public long GrossAmount { get; private set; }
    public long FeeAmount { get; private set; }
    public long NetAmount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public PayoutStatus Status { get; private set; } = PayoutStatus.Pending;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ExecutedAt { get; private set; }
    public string? FailureReason { get; private set; }

    private readonly List<PayoutItem> _items = [];
    public IReadOnlyCollection<PayoutItem> Items => _items.AsReadOnly();

    private MerchantPayoutBatch() { } // EF Core

    public MerchantPayoutBatch(
        string id,
        string merchantId,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        string currency,
        DateTimeOffset? createdAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(merchantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        Id = id;
        MerchantId = merchantId;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        Currency = currency.Trim().ToUpperInvariant();
        Status = PayoutStatus.Pending;
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow;
        GrossAmount = 0;
        FeeAmount = 0;
        NetAmount = 0;
    }

    public void AddItem(PayoutItem item)
    {
        if (Status != PayoutStatus.Pending)
        {
            throw new InvalidOperationException($"Cannot add items to payout batch in state '{Status}'.");
        }

        _items.Add(item);
        GrossAmount += item.GrossAmount;
        FeeAmount += item.FeeAmount;
        NetAmount = Math.Max(0, GrossAmount - FeeAmount);
    }

    public void MarkProcessing()
    {
        if (Status != PayoutStatus.Pending)
        {
            throw new InvalidOperationException($"Cannot transition payout batch to Processing from '{Status}'.");
        }

        Status = PayoutStatus.Processing;
    }

    public void MarkCompleted(DateTimeOffset? executedAt = null)
    {
        if (Status != PayoutStatus.Processing && Status != PayoutStatus.Pending)
        {
            throw new InvalidOperationException($"Cannot complete payout batch from '{Status}'.");
        }

        Status = PayoutStatus.Completed;
        ExecutedAt = executedAt ?? DateTimeOffset.UtcNow;
        FailureReason = null;
    }

    public void MarkFailed(string reason)
    {
        Status = PayoutStatus.Failed;
        FailureReason = reason;
    }
}
