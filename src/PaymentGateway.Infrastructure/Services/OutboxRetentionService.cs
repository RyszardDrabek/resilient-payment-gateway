using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.Events;
using PaymentGateway.Infrastructure.Options;
using PaymentGateway.Infrastructure.Persistence;

namespace PaymentGateway.Infrastructure.Services;

public sealed class OutboxRetentionService(
    PaymentDbContext dbContext,
    IOptions<OutboxOptions> options,
    ILogger<OutboxRetentionService> logger,
    TimeProvider? timeProvider = null) : IOutboxRetentionService
{
    private readonly OutboxOptions _options = options.Value;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<int> CleanupExpiredOutboxMessagesAsync(CancellationToken ct = default)
    {
        var retentionDays = _options.RetentionDays > 0 ? _options.RetentionDays : 14;
        var cutoff = _timeProvider.GetUtcNow().AddDays(-retentionDays);

        logger.LogInformation("Starting outbox retention cleanup for messages processed before {Cutoff} (retention: {RetentionDays} days)", cutoff, retentionDays);

        // Direct batch deletion without loading deleted message bodies or change tracker entities into memory
        var deletedCount = await dbContext.OutboxMessages
            .Where(m => m.ProcessedAt != null && m.ProcessedAt < cutoff)
            .ExecuteDeleteAsync(ct);

        logger.LogInformation("Outbox retention cleanup finished. Pruned {DeletedCount} messages.", deletedCount);
        return deletedCount;
    }
}
