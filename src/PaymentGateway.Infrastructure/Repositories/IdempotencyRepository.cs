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
        return await dbContext.IdempotencyRecords
            .Where(r => r.ExpiresAt < now)
            .ExecuteDeleteAsync(ct);
    }

    public async Task<int> ReapAbandonedLeasesAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        return await dbContext.IdempotencyRecords
            .Where(r => r.Status == PaymentGateway.Domain.Enums.IdempotencyStatus.InFlight
                        && r.LockedUntil != null
                        && r.LockedUntil < now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, PaymentGateway.Domain.Enums.IdempotencyStatus.Failed)
                .SetProperty(r => r.ResponseStatusCode, 504)
                .SetProperty(r => r.ResponsePayload, "{\"error\":\"In-flight idempotency lease expired without completion.\"}")
                .SetProperty(r => r.LockedUntil, (DateTimeOffset?)null), ct);
    }
}
