using Microsoft.EntityFrameworkCore;
using PaymentGateway.Application.Risk.Ports;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Infrastructure.Persistence;

namespace PaymentGateway.Infrastructure.Repositories;

public sealed class RiskFlagRepository(RiskDbContext db) : IRiskFlagRepository
{
    public Task<RiskFlag?> GetByIdAsync(string id, CancellationToken ct = default) =>
        db.RiskFlags.FirstOrDefaultAsync(f => f.Id == id, ct);

    public async Task<IReadOnlyList<RiskFlag>> GetByPaymentIdAsync(string paymentId, CancellationToken ct = default) =>
        await db.RiskFlags.AsNoTracking()
            .Where(f => f.PaymentId == paymentId)
            .OrderByDescending(f => f.RaisedAt)
            .ToListAsync(ct);

    public async Task<(IReadOnlyList<RiskFlag> Items, int TotalCount)> GetOpenFlagsAsync(int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        var query = db.RiskFlags.AsNoTracking()
            .Where(f => f.Status == "Open" && f.Disposition == null);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(f => f.RaisedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task SaveAsync(RiskFlag flag, CancellationToken ct = default)
    {
        var existing = await db.RiskFlags.FirstOrDefaultAsync(f => f.Id == flag.Id, ct);
        if (existing is null)
        {
            await db.RiskFlags.AddAsync(flag, ct);
        }
        else if (!ReferenceEquals(existing, flag))
        {
            db.Entry(existing).CurrentValues.SetValues(flag);
        }
        await db.SaveChangesAsync(ct);
    }
}
