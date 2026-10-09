using Microsoft.EntityFrameworkCore;
using PaymentGateway.Application.Risk.Ports;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Infrastructure.Persistence;

namespace PaymentGateway.Infrastructure.Repositories;

public sealed class RiskFlagRepository(RiskDbContext db) : IRiskFlagRepository
{
    public Task<RiskFlag?> GetByIdAsync(string id, CancellationToken ct = default) =>
        db.RiskFlags.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);

    public async Task<IReadOnlyList<RiskFlag>> GetByPaymentIdAsync(string paymentId, CancellationToken ct = default) =>
        await db.RiskFlags.AsNoTracking()
            .Where(f => f.PaymentId == paymentId)
            .OrderByDescending(f => f.RaisedAt)
            .ToListAsync(ct);

    public async Task SaveAsync(RiskFlag flag, CancellationToken ct = default)
    {
        var existing = await db.RiskFlags.FirstOrDefaultAsync(f => f.Id == flag.Id, ct);
        if (existing is null)
        {
            await db.RiskFlags.AddAsync(flag, ct);
        }
        await db.SaveChangesAsync(ct);
    }
}
