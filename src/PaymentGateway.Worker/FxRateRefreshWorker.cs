using MassTransit;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.Events;
using PaymentGateway.Application.Locking;
using PaymentGateway.Infrastructure.Fx;
using PaymentGateway.Infrastructure.Options;

namespace PaymentGateway.Worker;

public sealed class FxRateRefreshWorker(
    CachedFxRateProvider rateProvider,
    IOptions<FxRateRefreshOptions> options,
    ILogger<FxRateRefreshWorker> logger,
    IDistributedLockProvider? lockProvider = null,
    IPublishEndpoint? publishEndpoint = null) : BackgroundService
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

    public async Task ProcessCycleAsync(CancellationToken ct = default)
    {
        // Reference baseline rates verification and refreshment
        // Can be extended to fetch from ECB/live oracles.
        logger.LogDebug("Refreshing reference FX rates.");
        var freshRates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["USD"] = 1.0000m,
            ["USDC"] = 1.0000m,
            ["USDT"] = 1.0000m,
            ["EUR"] = 1.0850m,
            ["GBP"] = 1.2800m,
            ["PLN"] = 0.2520m,
            ["CHF"] = 1.1300m
        };

        foreach (var (currency, rate) in freshRates)
        {
            rateProvider.SetRateToUsd(currency, rate);
        }

        if (publishEndpoint is not null)
        {
            try
            {
                await publishEndpoint.Publish(new FxRatesRefreshedEvent(freshRates, DateTimeOffset.UtcNow), ct);
                logger.LogDebug("Published FxRatesRefreshedEvent across MassTransit message bus.");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to publish FxRatesRefreshedEvent across message bus.");
            }
        }
    }
}
