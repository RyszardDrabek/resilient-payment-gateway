using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.Idempotency;
using PaymentGateway.Infrastructure.Options;

namespace PaymentGateway.Worker;

public sealed class IdempotencyRetentionCleanupWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<IdempotencyRetentionOptions> options,
    ILogger<IdempotencyRetentionCleanupWorker> logger,
    PaymentGateway.Application.Locking.IDistributedLockProvider? lockProvider = null) : BackgroundService
{
    private readonly IdempotencyRetentionOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("IdempotencyRetentionCleanupWorker is disabled via configuration.");
            return;
        }

        var intervalMinutes = _options.CleanupIntervalMinutes > 0 ? _options.CleanupIntervalMinutes : 15;
        logger.LogInformation(
            "IdempotencyRetentionCleanupWorker started. Interval: {IntervalMinutes}m",
            intervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (lockProvider is not null)
                {
                    await using var lockHandle = await lockProvider.TryAcquireLockAsync(nameof(IdempotencyRetentionCleanupWorker), stoppingToken);
                    if (lockHandle is null)
                    {
                        logger.LogDebug("{Worker} cycle skipped; lock held by another instance.", nameof(IdempotencyRetentionCleanupWorker));
                    }
                    else
                    {
                        var (pruned, reaped) = await ProcessCycleAsync(stoppingToken);
                        if (pruned > 0 || reaped > 0)
                        {
                            logger.LogInformation(
                                "Idempotency retention cycle finished. Pruned {Pruned} expired records, reaped {Reaped} abandoned leases.",
                                pruned,
                                reaped);
                        }
                    }
                }
                else
                {
                    var (pruned, reaped) = await ProcessCycleAsync(stoppingToken);
                    if (pruned > 0 || reaped > 0)
                    {
                        logger.LogInformation(
                            "Idempotency retention cycle finished. Pruned {Pruned} expired records, reaped {Reaped} abandoned leases.",
                            pruned,
                            reaped);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Operational failure during idempotency retention cleanup cycle.");
            }

            try
            {
                var delay = TimeSpan.FromMinutes(intervalMinutes);
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        logger.LogInformation("IdempotencyRetentionCleanupWorker stopped.");
    }

    public async Task<(int Pruned, int Reaped)> ProcessCycleAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var retentionService = scope.ServiceProvider.GetRequiredService<IIdempotencyRetentionService>();

        var pruned = await retentionService.CleanupExpiredRecordsAsync(ct);
        var reaped = await retentionService.ReapAbandonedLeasesAsync(ct);

        return (pruned, reaped);
    }
}
