using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PaymentGateway.Application.Payments;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Worker;

public sealed class SubscriptionBillingWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<SubscriptionBillingWorker> logger,
    PaymentGateway.Application.Locking.IDistributedLockProvider? lockProvider = null) : BackgroundService
{
    private static readonly TimeSpan DefaultCheckInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("SubscriptionBillingWorker started. Interval: {Interval}s", DefaultCheckInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (lockProvider is not null)
                {
                    await using var lockHandle = await lockProvider.TryAcquireLockAsync(nameof(SubscriptionBillingWorker), stoppingToken);
                    if (lockHandle is null)
                    {
                        logger.LogDebug("{Worker} cycle skipped; lock held by another instance.", nameof(SubscriptionBillingWorker));
                    }
                    else
                    {
                        var billedCount = await ProcessBillingCycleAsync(stoppingToken);
                        if (billedCount > 0)
                        {
                            logger.LogInformation("Processed subscription billing cycle. Evaluated {Count} due subscriptions.", billedCount);
                        }
                    }
                }
                else
                {
                    var billedCount = await ProcessBillingCycleAsync(stoppingToken);
                    if (billedCount > 0)
                    {
                        logger.LogInformation("Processed subscription billing cycle. Evaluated {Count} due subscriptions.", billedCount);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred during subscription billing cycle.");
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

        logger.LogInformation("SubscriptionBillingWorker stopped.");
    }

    public async Task<int> ProcessBillingCycleAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var subscriptionRepo = scope.ServiceProvider.GetRequiredService<ISubscriptionRepository>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var dueSubscriptions = await subscriptionRepo.ListDueSubscriptionsAsync(DateTimeOffset.UtcNow, limit: 50, ct);
        if (dueSubscriptions.Count == 0)
        {
            return 0;
        }

        foreach (var sub in dueSubscriptions)
        {
            var idempotencyKey = $"sub_bill_{sub.Id}_{sub.NextBillingAt:yyyyMMddHHmmss}";
            var command = new AuthorizePaymentCommand(
                IdempotencyKey: idempotencyKey,
                PartyId: sub.PartyId,
                Amount: sub.Amount,
                Currency: sub.Currency,
                SettlementChannel: null,
                TransactionOrigin: "MIT",
                PaymentMethodId: sub.PaymentMethodId,
                SubscriptionId: sub.Id,
                StorePaymentMethod: false);

            try
            {
                var paymentDto = await mediator.Send(command, ct);
                if (string.Equals(paymentDto.State, "Authorized", StringComparison.OrdinalIgnoreCase))
                {
                    sub.RecordBillingSuccess(DateTimeOffset.UtcNow);
                    logger.LogInformation("Successfully billed subscription {SubscriptionId} (Payment {PaymentId})", sub.Id, paymentDto.PaymentId);
                }
                else
                {
                    sub.RecordBillingFailure(maxRetries: 3);
                    logger.LogWarning("Failed recurring charge for subscription {SubscriptionId}: payment state was '{State}'", sub.Id, paymentDto.State);
                }
            }
            catch (Exception ex)
            {
                sub.RecordBillingFailure(maxRetries: 3);
                logger.LogError(ex, "Exception while executing recurring charge for subscription {SubscriptionId}", sub.Id);
            }

            await subscriptionRepo.SaveAsync(sub, ct);
        }

        return dueSubscriptions.Count;
    }
}
