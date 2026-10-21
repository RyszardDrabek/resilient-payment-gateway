using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.Events;
using PaymentGateway.Infrastructure.Options;

namespace PaymentGateway.Worker;

public sealed class OutboxRetentionCleanupWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    ILogger<OutboxRetentionCleanupWorker> logger,
    PaymentGateway.Application.Locking.IDistributedLockProvider? lockProvider = null) : BackgroundService
{
    private readonly OutboxOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "OutboxRetentionCleanupWorker started. RetentionDays: {RetentionDays}, Interval: {IntervalHours}h",
            _options.RetentionDays,
            _options.RetentionCleanupIntervalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (lockProvider is not null)
                {
                    await using var lockHandle = await lockProvider.TryAcquireLockAsync(nameof(OutboxRetentionCleanupWorker), stoppingToken);
                    if (lockHandle is null)
                    {
                        logger.LogDebug("{Worker} cycle skipped; lock held by another instance.", nameof(OutboxRetentionCleanupWorker));
                    }
                    else
                    {
                        var pruned = await ProcessRetentionCleanupCycleAsync(stoppingToken);
                        logger.LogInformation("Outbox retention cleanup finished. Pruned {Count} expired outbox messages.", pruned);
                    }
                }
                else
                {
                    var pruned = await ProcessRetentionCleanupCycleAsync(stoppingToken);
                    logger.LogInformation("Outbox retention cleanup finished. Pruned {Count} expired outbox messages.", pruned);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // AC-4: Log operational failure and resume on next interval without terminating worker host
                logger.LogError(ex, "Operational failure during outbox retention cleanup cycle.");
            }

            try
            {
                var delay = TimeSpan.FromHours(_options.RetentionCleanupIntervalHours > 0 ? _options.RetentionCleanupIntervalHours : 24);
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        logger.LogInformation("OutboxRetentionCleanupWorker stopped.");
    }

    public async Task<int> ProcessRetentionCleanupCycleAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var retentionService = scope.ServiceProvider.GetRequiredService<IOutboxRetentionService>();
        return await retentionService.CleanupExpiredOutboxMessagesAsync(ct);
    }
}
