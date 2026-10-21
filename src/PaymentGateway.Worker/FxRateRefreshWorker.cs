using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.Locking;
using PaymentGateway.Infrastructure.Fx;
using PaymentGateway.Infrastructure.Options;

namespace PaymentGateway.Worker;

public sealed class FxRateRefreshWorker(
    CachedFxRateProvider rateProvider,
    IOptions<FxRateRefreshOptions> options,
    ILogger<FxRateRefreshWorker> logger,
    IDistributedLockProvider? lockProvider = null) : BackgroundService
{
    private readonly FxRateRefreshOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("FxRateRefreshWorker is disabled via configuration.");
            return;
        }

        var intervalSeconds = _options.IntervalSeconds > 0 ? _options.IntervalSeconds : 300;
        logger.LogInformation("FxRateRefreshWorker started. Interval: {IntervalSeconds}s", intervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (lockProvider is not null)
                {
                    await using var lockHandle = await lockProvider.TryAcquireLockAsync(nameof(FxRateRefreshWorker), stoppingToken);
                    if (lockHandle is null)
                    {
                        logger.LogDebug("{Worker} cycle skipped; lock held by another instance.", nameof(FxRateRefreshWorker));
                    }
                    else
                    {
                        await ProcessCycleAsync(stoppingToken);
                    }
                }
                else
                {
                    await ProcessCycleAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred during FX rate refresh cycle.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        logger.LogInformation("FxRateRefreshWorker stopped.");
    }

    public Task ProcessCycleAsync(CancellationToken ct = default)
    {
        // Reference baseline rates verification and refreshment
        // In this implementation, ensures baseline reference rates remain populated and healthy.
        // Can be extended to fetch from ECB/live oracles.
        logger.LogDebug("Refreshing reference FX rates.");
        rateProvider.SetRateToUsd("USD", 1.0000m);
        rateProvider.SetRateToUsd("USDC", 1.0000m);
        rateProvider.SetRateToUsd("USDT", 1.0000m);
        rateProvider.SetRateToUsd("EUR", 1.0850m);
        rateProvider.SetRateToUsd("GBP", 1.2800m);
        rateProvider.SetRateToUsd("PLN", 0.2520m);
        rateProvider.SetRateToUsd("CHF", 1.1300m);
        return Task.CompletedTask;
    }
}
