using PaymentGateway.Domain.Entities;

namespace PaymentGateway.Domain.Ports;

public interface IIdempotencyRepository
{
    Task<IdempotencyRecord?> FindAsync(string key, string commandType, string? paymentId, CancellationToken ct);
    Task AddAsync(IdempotencyRecord record, CancellationToken ct);
    Task UpdateAsync(IdempotencyRecord record, CancellationToken ct);

    Task<int> CleanupExpiredAsync(DateTimeOffset now, CancellationToken ct = default) => Task.FromResult(0);
    Task<int> ReapAbandonedLeasesAsync(DateTimeOffset now, CancellationToken ct = default) => Task.FromResult(0);
}
