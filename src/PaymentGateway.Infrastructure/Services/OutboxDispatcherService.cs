using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.Events;
using PaymentGateway.Infrastructure.Options;
using PaymentGateway.Infrastructure.Persistence;

namespace PaymentGateway.Infrastructure.Services;

public sealed class OutboxDispatcherService(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    ILogger<OutboxDispatcherService> logger) : BackgroundService
{
    private readonly OutboxOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("OutboxDispatcherService is disabled via configuration.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingMessagesAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error while processing outbox messages.");
            }

            await Task.Delay(_options.PollingIntervalMs, stoppingToken);
        }
    }

    public async Task<int> ProcessPendingMessagesAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var pendingMessages = await dbContext.OutboxMessages
            .Where(m => m.ProcessedAt == null && m.DeliveryAttempts < _options.MaxDeliveryAttempts)
            .OrderBy(m => m.CreatedAt)
            .Take(_options.BatchSize)
            .ToListAsync(ct);

        if (pendingMessages.Count == 0)
        {
            return 0;
        }

        foreach (var message in pendingMessages)
        {
            message.DeliveryAttempts++;
            try
            {
                if (message.EventType == PaymentLifecycleEvent.EventType ||
                    message.EventType == typeof(PaymentLifecycleEvent).FullName ||
                    message.EventType == nameof(PaymentLifecycleEvent))
                {
                    var evt = JsonSerializer.Deserialize<PaymentLifecycleEvent>(message.Payload);
                    if (evt is not null)
                    {
                        System.Diagnostics.ActivityContext parentContext = default;
                        if (!string.IsNullOrWhiteSpace(evt.TraceParent) &&
                            System.Diagnostics.ActivityContext.TryParse(evt.TraceParent, null, out var parsed))
                        {
                            parentContext = parsed;
                        }

                        using var activity = PaymentGateway.Application.Common.PaymentTracing.ActivitySource.StartActivity(
                            "Outbox.Dispatch",
                            System.Diagnostics.ActivityKind.Producer,
                            parentContext);

                        activity?.SetTag("outbox.message_id", message.Id.ToString());
                        activity?.SetTag("payment.id", evt.PaymentId);
                        activity?.SetTag("payment.party_id", evt.PartyId);

                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        await publishEndpoint.Publish(evt, ct);
                        sw.Stop();

                        PaymentGateway.Application.Metrics.PaymentMetrics.RecordOutboxDispatch("success");
                        PaymentGateway.Application.Metrics.PaymentMetrics.RecordOutboxDuration(sw.Elapsed.TotalSeconds);
                    }
                }

                message.ProcessedAt = DateTimeOffset.UtcNow;
                message.Error = null;
            }
            catch (Exception ex)
            {
                PaymentGateway.Application.Metrics.PaymentMetrics.RecordOutboxDispatch("failure");
                logger.LogError(
                    ex,
                    "Failed to dispatch outbox message {MessageId} (Attempt {Attempt}/{MaxAttempts})",
                    message.Id,
                    message.DeliveryAttempts,
                    _options.MaxDeliveryAttempts);

                message.Error = ex.Message;

                if (message.DeliveryAttempts >= _options.MaxDeliveryAttempts)
                {
                    PaymentGateway.Application.Metrics.PaymentMetrics.RecordOutboxDeadLettered();
                    logger.LogCritical(
                        "Outbox message {MessageId} exceeded max delivery attempts ({MaxAttempts}) and is dead-lettered.",
                        message.Id,
                        _options.MaxDeliveryAttempts);
                }
            }
        }

        await dbContext.SaveChangesAsync(ct);
        return pendingMessages.Count;
    }
}
