using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PaymentGateway.Application.Events;
using PaymentGateway.Infrastructure.Persistence;

namespace PaymentGateway.Infrastructure.Services;

public sealed class OutboxDispatcherService(
    IServiceScopeFactory scopeFactory,
    ILogger<OutboxDispatcherService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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

            await Task.Delay(100, stoppingToken);
        }
    }

    public async Task<int> ProcessPendingMessagesAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var pendingMessages = await dbContext.OutboxMessages
            .Where(m => m.ProcessedAt == null)
            .OrderBy(m => m.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        if (pendingMessages.Count == 0)
        {
            return 0;
        }

        foreach (var message in pendingMessages)
        {
            try
            {
                if (message.EventType == typeof(PaymentLifecycleEvent).FullName ||
                    message.EventType == nameof(PaymentLifecycleEvent))
                {
                    var evt = JsonSerializer.Deserialize<PaymentLifecycleEvent>(message.Payload);
                    if (evt is not null)
                    {
                        await publishEndpoint.Publish(evt, ct);
                    }
                }

                message.ProcessedAt = DateTimeOffset.UtcNow;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to dispatch outbox message {MessageId}", message.Id);
                message.Error = ex.Message;
            }
        }

        await dbContext.SaveChangesAsync(ct);
        return pendingMessages.Count;
    }
}
