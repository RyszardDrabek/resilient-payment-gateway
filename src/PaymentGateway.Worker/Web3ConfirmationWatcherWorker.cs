using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PaymentGateway.Application.Web3;

namespace PaymentGateway.Worker;

public sealed class Web3ConfirmationWatcherWorker(
    IServiceProvider serviceProvider,
    ILogger<Web3ConfirmationWatcherWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Web3ConfirmationWatcherWorker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var watcherService = scope.ServiceProvider.GetRequiredService<IWeb3FinalityWatcherService>();
                var results = await watcherService.WatchAllPendingSettlementsAsync(stoppingToken);
                if (results.Count > 0)
                {
                    logger.LogInformation("Web3ConfirmationWatcherWorker processed {Count} pending settlements", results.Count);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error while observing pending Web3 settlements");
            }

            try
            {
                await Task.Delay(PollingInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        logger.LogInformation("Web3ConfirmationWatcherWorker stopped");
    }
}
