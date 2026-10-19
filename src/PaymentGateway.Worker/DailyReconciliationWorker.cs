using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PaymentGateway.Application.Payouts;
using PaymentGateway.Application.Reconciliation;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Worker;

public sealed class DailyReconciliationWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<DailyReconciliationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan DefaultCheckInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("DailyReconciliationWorker started. Check interval: {Interval} minutes", DefaultCheckInterval.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await ProcessReconciliationCycleAsync(ct: stoppingToken);
                if (result.ReconciledCount > 0 || result.BatchesGenerated > 0)
                {
                    logger.LogInformation(
                        "Reconciliation cycle completed: {Reconciled} payments reconciled, {Discrepancies} discrepancies, {Batches} payout batches generated.",
                        result.ReconciledCount,
                        result.DiscrepancyCount,
                        result.BatchesGenerated);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred during daily reconciliation & payout cycle.");
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

        logger.LogInformation("DailyReconciliationWorker stopped.");
    }

    public async Task<(int ReconciledCount, int DiscrepancyCount, int BatchesGenerated)> ProcessReconciliationCycleAsync(
        DateTimeOffset? asOf = null,
        CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var paymentRepo = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();

        var to = asOf ?? DateTimeOffset.UtcNow;
        var from = to.AddDays(-1);

        // 1. Run reconciliation for payments captured in the settlement window
        var reconResult = await mediator.Send(new RunReconciliationCommand(from, to), ct);

        // 2. Discover merchants with captured payments to aggregate payout batches
        var capturedPayments = await paymentRepo.GetCapturedPaymentsAsync(from, to, ct: ct);
        var merchantGroups = capturedPayments
            .GroupBy(p => new { p.PartyId, p.Currency });

        var batchesGenerated = 0;
        foreach (var group in merchantGroups)
        {
            try
            {
                await mediator.Send(new GeneratePayoutBatchCommand(
                    group.Key.PartyId,
                    from,
                    to,
                    group.Key.Currency), ct);

                batchesGenerated++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to generate payout batch for merchant {MerchantId} ({Currency}).", group.Key.PartyId, group.Key.Currency);
            }
        }

        return (reconResult.TotalProcessed, reconResult.DiscrepancyCount, batchesGenerated);
    }
}
