namespace PaymentGateway.Domain.Entities;

public sealed class ReconciliationRecord
{
    public string Id { get; private set; } = string.Empty;
    public string? PaymentId { get; private set; }
    public string Channel { get; private set; } = string.Empty;
    public string ChannelReference { get; private set; } = string.Empty;
    public long GatewayAmount { get; private set; }
    public long AcquirerAmount { get; private set; }
    public DiscrepancyType DiscrepancyType { get; private set; }
    public ReconciliationRecordStatus Status { get; private set; }
    public DateTimeOffset ReconciledAt { get; private set; }
    public string? Notes { get; private set; }

    private ReconciliationRecord() { } // EF Core

    public ReconciliationRecord(
        string id,
        string? paymentId,
        string channel,
        string channelReference,
        long gatewayAmount,
        long acquirerAmount,
        DiscrepancyType discrepancyType,
        ReconciliationRecordStatus status,
        DateTimeOffset? reconciledAt = null,
        string? notes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ArgumentException.ThrowIfNullOrWhiteSpace(channelReference);

        Id = id;
        PaymentId = paymentId;
        Channel = channel;
        ChannelReference = channelReference;
        GatewayAmount = gatewayAmount;
        AcquirerAmount = acquirerAmount;
        DiscrepancyType = discrepancyType;
        Status = status;
        ReconciledAt = reconciledAt ?? DateTimeOffset.UtcNow;
        Notes = notes;
    }

    public static ReconciliationRecord CreateMatched(
        string paymentId,
        string channel,
        string channelReference,
        long amount)
    {
        return new ReconciliationRecord(
            $"rec_{Guid.NewGuid():N}",
            paymentId,
            channel,
            channelReference,
            gatewayAmount: amount,
            acquirerAmount: amount,
            DiscrepancyType.None,
            ReconciliationRecordStatus.Matched);
    }

    public static ReconciliationRecord CreateDiscrepancy(
        string? paymentId,
        string channel,
        string channelReference,
        long gatewayAmount,
        long acquirerAmount,
        DiscrepancyType discrepancyType,
        string? notes = null)
    {
        return new ReconciliationRecord(
            $"rec_{Guid.NewGuid():N}",
            paymentId,
            channel,
            channelReference,
            gatewayAmount,
            acquirerAmount,
            discrepancyType,
            ReconciliationRecordStatus.DiscrepancyFlagged,
            notes: notes);
    }

    public void Resolve(string? resolutionNotes = null)
    {
        Status = ReconciliationRecordStatus.Resolved;
        if (!string.IsNullOrWhiteSpace(resolutionNotes))
        {
            Notes = string.IsNullOrWhiteSpace(Notes) ? resolutionNotes : $"{Notes}; {resolutionNotes}";
        }
    }
}
