using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.Webhooks;
using PaymentGateway.Infrastructure.Options;

namespace PaymentGateway.Worker;

public sealed class MerchantWebhookRetentionCleanupWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<MerchantWebhookOptions> options,
    ILogger<MerchantWebhookRetentionCleanupWorker> logger,
    PaymentGateway.Application.Locking.IDistributedLockProvider? lockProvider = null) : BackgroundService
{
    private readonly MerchantWebhookOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalHours = _options.RetentionCleanupIntervalHours > 0 ? _options.RetentionCleanupIntervalHours : 24;
        logger.LogInformation(
            "MerchantWebhookRetentionCleanupWorker started. RetentionDays: {RetentionDays}, Interval: {IntervalHours}h",
            _options.DeadLetterRetentionDays,
            intervalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (lockProvider is not null)
                {
                    await using var lockHandle = await lockProvider.TryAcquireLockAsync(nameof(MerchantWebhookRetentionCleanupWorker), stoppingToken);
                    if (lockHandle is null)
                    {
                        logger.LogDebug("{Worker} cycle skipped; lock held by another instance.", nameof(MerchantWebhookRetentionCleanupWorker));
                    }
                    else
                    {
                        var pruned = await ProcessCycleAsync(stoppingToken);
                        if (pruned > 0)
                        {
                            logger.LogInformation("Merchant webhook dead-letter retention cycle pruned {Count} records.", pruned);
                        }
                    }
                }
                else
                {
                    var pruned = await ProcessCycleAsync(stoppingToken);
                    if (pruned > 0)
                    {
                        logger.LogInformation("Merchant webhook dead-letter retention cycle pruned {Count} records.", pruned);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Operational failure during merchant webhook dead-letter retention cycle.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(intervalHours), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        logger.LogInformation("MerchantWebhookRetentionCleanupWorker stopped.");
    }

    public async Task<int> ProcessCycleAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var retentionService = scope.ServiceProvider.GetRequiredService<IMerchantWebhookRetentionService>();
        return await retentionService.CleanupExpiredDeadLettersAsync(ct);
    }
}
