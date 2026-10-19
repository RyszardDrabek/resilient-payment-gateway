using PaymentGateway.Domain.Entities;

namespace PaymentGateway.Application.Reconciliation;

public record ReconciliationRecordDto(
    string Id,
    string? PaymentId,
    string Channel,
    string ChannelReference,
    long GatewayAmount,
    long AcquirerAmount,
    string DiscrepancyType,
    string Status,
    DateTimeOffset ReconciledAt,
    string? Notes);

public record ReconciliationSummaryDto(
    int TotalRecords,
    int MatchedCount,
    int DiscrepancyCount,
    long MatchedVolume,
    long DiscrepancyVolume,
    double MatchRatePercentage);

public static class ReconciliationMapper
{
    public static ReconciliationRecordDto ToDto(this ReconciliationRecord record) =>
        new(
            record.Id,
            record.PaymentId,
            record.Channel,
            record.ChannelReference,
            record.GatewayAmount,
            record.AcquirerAmount,
            record.DiscrepancyType.ToString(),
            record.Status.ToString(),
            record.ReconciledAt,
            record.Notes);
}
