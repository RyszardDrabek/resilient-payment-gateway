using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.Fx;
using PaymentGateway.Infrastructure.Options;

namespace PaymentGateway.Worker;

public sealed class FxQuoteExpirationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<FxQuoteExpirationOptions> options,
    ILogger<FxQuoteExpirationWorker> logger) : BackgroundService
{
    private readonly FxQuoteExpirationOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("FxQuoteExpirationWorker is disabled via configuration.");
            return;
        }

        var intervalSeconds = _options.IntervalSeconds > 0 ? _options.IntervalSeconds : 30;
        logger.LogInformation("FxQuoteExpirationWorker started. Interval: {IntervalSeconds}s", intervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var expiredCount = await ProcessCycleAsync(stoppingToken);
                if (expiredCount > 0)
                {
                    logger.LogInformation("FX quote expiration cycle expired {Count} stale quotes.", expiredCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred during FX quote expiration cycle.");
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

        logger.LogInformation("FxQuoteExpirationWorker stopped.");
    }

    private const int MaxDrainIterationsPerCycle = 5;

    public async Task<int> ProcessCycleAsync(CancellationToken ct = default)
    {
        var batchSize = _options.BatchSize > 0 ? _options.BatchSize : 1000;
        var totalExpired = 0;

        for (var iteration = 0; iteration < MaxDrainIterationsPerCycle; iteration++)
        {
            using var scope = scopeFactory.CreateScope();
            var expirationService = scope.ServiceProvider.GetRequiredService<IFxQuoteExpirationService>();
            var expiredThisBatch = await expirationService.ExpireStaleQuotesAsync(ct);

            totalExpired += expiredThisBatch;

            if (expiredThisBatch < batchSize)
            {
                break;
            }
        }

        return totalExpired;
    }
}
