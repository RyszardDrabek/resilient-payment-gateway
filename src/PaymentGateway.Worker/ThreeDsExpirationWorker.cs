using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PaymentGateway.Application.Metrics;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Worker;

public sealed class ThreeDsExpirationWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ThreeDsExpirationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan DefaultCheckInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ThreeDsExpirationWorker started. Interval: {Interval}s", DefaultCheckInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var expiredCount = await ProcessExpirationCycleAsync(stoppingToken);
                if (expiredCount > 0)
                {
                    logger.LogInformation("Processed 3DS challenge expiration cycle. Expired {Count} abandoned challenges.", expiredCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred during 3DS challenge expiration cycle.");
            }

            try
            {
                await Task.Delay(DefaultCheckInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        logger.LogInformation("ThreeDsExpirationWorker stopped.");
    }

    public async Task<int> ProcessExpirationCycleAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();

        var expiredPayments = await repository.GetExpiredThreeDsPaymentsAsync(DateTimeOffset.UtcNow, ct);
        if (expiredPayments.Count == 0)
        {
            return 0;
        }

        foreach (var payment in expiredPayments)
        {
            try
            {
                payment.ExpireThreeDs();
                await repository.UpdateAsync(payment, ct);
                PaymentMetrics.RecordTransaction("Declined", payment.SettlementChannel, "3ds_expire");
                PaymentMetrics.RecordDecline("ThreeDsExpired", payment.SettlementChannel);
                logger.LogWarning("Expired abandoned 3DS challenge for payment {PaymentId}. State changed to Declined.", payment.Id);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to expire 3DS challenge for payment {PaymentId}.", payment.Id);
            }
        }

        return expiredPayments.Count;
    }
}
