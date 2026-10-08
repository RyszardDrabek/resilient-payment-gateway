namespace PaymentGateway.Domain.Entities;

public sealed class AdyenNotification
{
    private AdyenNotification()
    {
        PspReference = string.Empty;
        MerchantAccountCode = string.Empty;
        MerchantReference = string.Empty;
        EventCode = string.Empty;
        AmountCurrency = string.Empty;
        Status = "Received";
    }

    public Guid Id { get; private init; }
    public string PspReference { get; private init; }
    public string? OriginalReference { get; private init; }
    public string MerchantAccountCode { get; private init; }
    public string MerchantReference { get; private init; }
    public string EventCode { get; private init; }
    public DateTimeOffset? EventDate { get; private init; }
    public long AmountValue { get; private init; }
    public string AmountCurrency { get; private init; }
    public bool Success { get; private init; }
    public string? Reason { get; private init; }
    public string? CorrelatedPaymentId { get; private set; }
    public string Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private init; }

    public static AdyenNotification Create(
        string pspReference,
        string? originalReference,
        string merchantAccountCode,
        string merchantReference,
        string eventCode,
        DateTimeOffset? eventDate,
        long amountValue,
        string amountCurrency,
        bool success,
        string? reason,
        string? correlatedPaymentId = null,
        DateTimeOffset? now = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pspReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(merchantAccountCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(merchantReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventCode);

        return new AdyenNotification
        {
            Id = Guid.NewGuid(),
            PspReference = pspReference,
            OriginalReference = originalReference,
            MerchantAccountCode = merchantAccountCode,
            MerchantReference = merchantReference,
            EventCode = eventCode,
            EventDate = eventDate,
            AmountValue = amountValue,
            AmountCurrency = amountCurrency ?? string.Empty,
            Success = success,
            Reason = reason,
            CorrelatedPaymentId = correlatedPaymentId,
            Status = "Received",
            CreatedAt = now ?? DateTimeOffset.UtcNow
        };
    }

    public void MarkCorrelated(string paymentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paymentId);
        CorrelatedPaymentId = paymentId;
    }

    public void MarkProcessed()
    {
        Status = "Processed";
    }
}
