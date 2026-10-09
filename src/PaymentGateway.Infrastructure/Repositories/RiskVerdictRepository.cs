using Microsoft.EntityFrameworkCore;
using PaymentGateway.Application.Risk.Ports;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Infrastructure.Persistence;

namespace PaymentGateway.Infrastructure.Repositories;

public sealed class RiskVerdictRepository(RiskDbContext db) : IRiskVerdictRepository
{
    public Task<RiskVerdict?> GetByEventIdAsync(string eventId, CancellationToken ct = default) =>
        db.RiskVerdicts.AsNoTracking().FirstOrDefaultAsync(v => v.EventId == eventId, ct);

    public Task<RiskVerdict?> GetByPaymentIdAsync(string paymentId, CancellationToken ct = default) =>
        db.RiskVerdicts.AsNoTracking().OrderByDescending(v => v.ScoredAt).FirstOrDefaultAsync(v => v.PaymentId == paymentId, ct);

    public Task<RiskVerdict?> GetByIdentifierAsync(string identifier, CancellationToken ct = default) =>
        db.RiskVerdicts.AsNoTracking()
            .Where(v => v.PaymentId == identifier || v.EventId == identifier || v.Id == identifier)
            .OrderByDescending(v => v.ScoredAt)
            .FirstOrDefaultAsync(ct);

    public async Task SaveAsync(RiskVerdict verdict, CancellationToken ct = default)
    {
        var existing = await db.RiskVerdicts.FirstOrDefaultAsync(v => v.Id == verdict.Id || v.EventId == verdict.EventId, ct);
        if (existing is null)
        {
            await db.RiskVerdicts.AddAsync(verdict, ct);
        }
        await db.SaveChangesAsync(ct);
    }
}
