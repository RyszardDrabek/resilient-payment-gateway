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
    IPublishEndpoint? publishEndpoint = null,
    PaymentGateway.Application.Fx.Ports.IExternalFxFeedClient? feedClient = null) : BackgroundService
{
    private readonly FxRateRefreshOptions _options = options.Value;

    private static readonly Dictionary<string, decimal> BaselineRates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["USD"] = 1.0000m,
        ["USDC"] = 1.0000m,
        ["USDT"] = 1.0000m,
        ["EUR"] = 1.0850m,
        ["GBP"] = 1.2800m,
        ["PLN"] = 0.2520m,
        ["CHF"] = 1.1300m
    };

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
        logger.LogDebug("Refreshing reference FX rates.");
        var freshRates = new Dictionary<string, decimal>(BaselineRates, StringComparer.OrdinalIgnoreCase);

        if (feedClient is not null)
        {
            try
            {
                var liveRates = await feedClient.FetchRatesToUsdAsync(ct);
                if (liveRates is not null)
                {
                    foreach (var (currency, rate) in liveRates)
                    {
                        freshRates[currency] = rate;
                    }
                    logger.LogInformation("Applied {Count} live FX rates from external feed.", liveRates.Count);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to retrieve live FX rates from external feed. Using baseline fallback.");
            }
        }

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
