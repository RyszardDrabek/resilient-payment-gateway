using Microsoft.Extensions.Logging;
using PaymentGateway.Application.Idempotency;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Infrastructure.Services;

public sealed class IdempotencyRetentionService(
    IIdempotencyRepository repository,
    ILogger<IdempotencyRetentionService> logger,
    TimeProvider? timeProvider = null) : IIdempotencyRetentionService
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<int> CleanupExpiredRecordsAsync(CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow();
        logger.LogInformation("Starting idempotency retention cleanup for records expired before {Now}", now);

        var deletedCount = await repository.CleanupExpiredAsync(now, ct);

        if (deletedCount > 0)
        {
            logger.LogInformation("Idempotency retention cleanup finished. Pruned {DeletedCount} expired records.", deletedCount);
        }

        return deletedCount;
    }

    public async Task<int> ReapAbandonedLeasesAsync(CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow();
        logger.LogInformation("Checking for abandoned in-flight idempotency leases before {Now}", now);

        var reapedCount = await repository.ReapAbandonedLeasesAsync(now, ct);

        if (reapedCount > 0)
        {
            logger.LogWarning("Reaped {ReapedCount} abandoned in-flight idempotency leases expired before {Now}.", reapedCount, now);
        }

        return reapedCount;
    }
}
