using Microsoft.EntityFrameworkCore;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Exceptions;
using PaymentGateway.Domain.Ports;
using PaymentGateway.Infrastructure.Persistence;

namespace PaymentGateway.Infrastructure.Repositories;

public sealed class IdempotencyRepository(PaymentDbContext dbContext) : IIdempotencyRepository
{
    public async Task<IdempotencyRecord?> FindAsync(string key, string commandType, string? paymentId, CancellationToken ct)
    {
        if (string.Equals(commandType, "Authorize", StringComparison.OrdinalIgnoreCase))
        {
            return await dbContext.IdempotencyRecords
                .FirstOrDefaultAsync(r => r.Key == key && r.CommandType == commandType, ct);
        }

        return await dbContext.IdempotencyRecords
            .FirstOrDefaultAsync(r => r.Key == key && r.CommandType == commandType && r.PaymentId == paymentId, ct);
    }

    public async Task AddAsync(IdempotencyRecord record, CancellationToken ct)
    {
        try
        {
            await dbContext.IdempotencyRecords.AddAsync(record, ct);
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            throw new IdempotencyInFlightException();
        }
    }

    public async Task UpdateAsync(IdempotencyRecord record, CancellationToken ct)
    {
        dbContext.IdempotencyRecords.Update(record);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<int> CleanupExpiredAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var expiredIds = await dbContext.IdempotencyRecords
            .Where(r => r.ExpiresAt < now)
            .OrderBy(r => r.ExpiresAt)
            .Select(r => r.Id)
            .Take(1000)
            .ToListAsync(ct);

        if (expiredIds.Count == 0)
        {
            return 0;
        }

        return await dbContext.IdempotencyRecords
            .Where(r => expiredIds.Contains(r.Id))
            .ExecuteDeleteAsync(ct);
    }

    public async Task<int> ReapAbandonedLeasesAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var abandonedIds = await dbContext.IdempotencyRecords
            .Where(r => r.Status == PaymentGateway.Domain.Enums.IdempotencyStatus.InFlight
                        && r.LockedUntil != null
                        && r.LockedUntil < now)
            .OrderBy(r => r.LockedUntil)
            .Select(r => r.Id)
            .Take(1000)
            .ToListAsync(ct);

        if (abandonedIds.Count == 0)
        {
            return 0;
        }

        return await dbContext.IdempotencyRecords
            .Where(r => abandonedIds.Contains(r.Id))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, PaymentGateway.Domain.Enums.IdempotencyStatus.Failed)
                .SetProperty(r => r.ResponseStatusCode, 504)
                .SetProperty(r => r.ResponsePayload, "{\"type\":\"https://httpstatuses.com/504\",\"title\":\"Gateway Timeout\",\"status\":504,\"detail\":\"In-flight idempotency lease expired without completion.\"}")
                .SetProperty(r => r.LockedUntil, (DateTimeOffset?)null), ct);
    }
}
