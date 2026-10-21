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
        var now = _timeProvider.GetUtcNow();
        var retentionDays = _options.RetentionDays > 0 ? _options.RetentionDays : 14;
        var processedCutoff = now.AddDays(-retentionDays);

        var deadLetterDays = _options.DeadLetterRetentionDays > 0 ? _options.DeadLetterRetentionDays : 30;
        var deadLetterCutoff = now.AddDays(-deadLetterDays);

        logger.LogInformation(
            "Starting outbox retention cleanup (processed cutoff: {ProcessedCutoff}, dead-letter cutoff: {DeadLetterCutoff})",
            processedCutoff,
            deadLetterCutoff);

        // Direct batch deletion without loading deleted message bodies or change tracker entities into memory
        var deletedProcessed = await dbContext.OutboxMessages
            .Where(m => m.ProcessedAt != null && m.ProcessedAt < processedCutoff)
            .ExecuteDeleteAsync(ct);

        var deletedDeadLettered = await dbContext.OutboxMessages
            .Where(m => m.ProcessedAt == null && m.DeliveryAttempts >= _options.MaxDeliveryAttempts && m.CreatedAt < deadLetterCutoff)
            .ExecuteDeleteAsync(ct);

        var totalDeleted = deletedProcessed + deletedDeadLettered;

        if (totalDeleted > 0)
        {
            logger.LogInformation(
                "Outbox retention cleanup finished. Pruned {ProcessedCount} processed and {DeadLetterCount} dead-lettered messages.",
                deletedProcessed,
                deletedDeadLettered);
        }

        return totalDeleted;
    }
}
