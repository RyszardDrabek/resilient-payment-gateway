using PaymentGateway.Domain.Entities;

namespace PaymentGateway.Domain.Ports;

public record ReconciliationSummary(
    int TotalRecords,
    int MatchedCount,
    int DiscrepancyCount,
    long MatchedVolume,
    long DiscrepancyVolume);

public interface IReconciliationRepository
{
    Task<ReconciliationRecord?> GetByIdAsync(string id, CancellationToken ct = default);
    Task SaveAsync(ReconciliationRecord record, CancellationToken ct = default);
    Task SaveBatchAsync(IEnumerable<ReconciliationRecord> records, CancellationToken ct = default);
    Task<IReadOnlyList<ReconciliationRecord>> ListDiscrepanciesAsync(int skip = 0, int take = 50, CancellationToken ct = default);
    Task<ReconciliationSummary> GetSummaryAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
