using Microsoft.EntityFrameworkCore;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Ports;
using PaymentGateway.Infrastructure.Persistence;

namespace PaymentGateway.Infrastructure.Repositories;

public sealed class ReconciliationRepository(PaymentDbContext dbContext) : IReconciliationRepository
{
    public async Task<ReconciliationRecord?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        return await dbContext.ReconciliationRecords.FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task SaveAsync(ReconciliationRecord record, CancellationToken ct = default)
    {
        var exists = await dbContext.ReconciliationRecords.AnyAsync(r => r.Id == record.Id, ct);
        if (!exists)
        {
            dbContext.ReconciliationRecords.Add(record);
        }
        else
        {
            dbContext.ReconciliationRecords.Update(record);
        }

        await dbContext.SaveChangesAsync(ct);
    }

    public async Task SaveBatchAsync(IEnumerable<ReconciliationRecord> records, CancellationToken ct = default)
    {
        foreach (var record in records)
        {
            var exists = await dbContext.ReconciliationRecords.AnyAsync(r => r.Id == record.Id, ct);
            if (!exists)
            {
                dbContext.ReconciliationRecords.Add(record);
            }
            else
            {
                dbContext.ReconciliationRecords.Update(record);
            }
        }

        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ReconciliationRecord>> ListDiscrepanciesAsync(int skip = 0, int take = 50, CancellationToken ct = default)
    {
        return await dbContext.ReconciliationRecords
            .Where(r => r.Status == ReconciliationRecordStatus.DiscrepancyFlagged)
            .OrderByDescending(r => r.ReconciledAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<ReconciliationSummary> GetSummaryAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var records = await dbContext.ReconciliationRecords
            .Where(r => r.ReconciledAt >= from && r.ReconciledAt <= to)
            .ToListAsync(ct);

        var total = records.Count;
        var matched = records.Where(r => r.Status == ReconciliationRecordStatus.Matched).ToList();
        var discrepancies = records.Where(r => r.Status == ReconciliationRecordStatus.DiscrepancyFlagged).ToList();

        return new ReconciliationSummary(
            TotalRecords: total,
            MatchedCount: matched.Count,
            DiscrepancyCount: discrepancies.Count,
            MatchedVolume: matched.Sum(r => r.GatewayAmount),
            DiscrepancyVolume: discrepancies.Sum(r => Math.Abs(r.GatewayAmount - r.AcquirerAmount)));
    }
}
