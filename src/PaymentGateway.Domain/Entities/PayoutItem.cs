namespace PaymentGateway.Domain.Entities;

public sealed class PayoutItem
{
    public string Id { get; private set; } = string.Empty;
    public string PayoutBatchId { get; private set; } = string.Empty;
    public string PaymentId { get; private set; } = string.Empty;
    public long GrossAmount { get; private set; }
    public long FeeAmount { get; private set; }
    public long NetAmount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public DateTimeOffset CapturedAt { get; private set; }

    private PayoutItem() { } // EF Core

    public PayoutItem(
        string id,
        string payoutBatchId,
        string paymentId,
        long grossAmount,
        long feeAmount,
        string currency,
        DateTimeOffset? capturedAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(payoutBatchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(paymentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        Id = id;
        PayoutBatchId = payoutBatchId;
        PaymentId = paymentId;
        GrossAmount = grossAmount;
        FeeAmount = feeAmount;
        NetAmount = Math.Max(0, grossAmount - feeAmount);
        Currency = currency.Trim().ToUpperInvariant();
        CapturedAt = capturedAt ?? DateTimeOffset.UtcNow;
    }
}
