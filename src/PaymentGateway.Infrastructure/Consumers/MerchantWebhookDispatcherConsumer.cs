using MassTransit;
using Microsoft.Extensions.Logging;
using PaymentGateway.Application.Events;
using PaymentGateway.Application.Webhooks;

namespace PaymentGateway.Infrastructure.Consumers;

public sealed class MerchantWebhookDispatcherConsumer(
    IMerchantWebhookDispatcher dispatcher,
    ILogger<MerchantWebhookDispatcherConsumer> logger,
    TimeProvider? timeProvider = null)
    : IConsumer<PaymentLifecycleEvent>
{
    private static readonly HashSet<string> TerminalOutcomes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Captured",
        "PartiallyCaptured",
        "Refunded",
        "PartiallyRefunded",
        "Cancelled",
        "Declined"
    };

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public Task Consume(ConsumeContext<PaymentLifecycleEvent> context) =>
        ProcessEventAsync(context.Message, context.CancellationToken, context);

    public async Task<bool> ProcessEventAsync(
        PaymentLifecycleEvent evt,
        CancellationToken cancellationToken = default,
        ConsumeContext? context = null)
    {
        // AC-1: Filter for terminal states only
        if (!IsTerminalState(evt.Outcome))
        {
            logger.LogDebug(
                "Event {EventId} for payment {PaymentId} has non-terminal outcome '{Outcome}'. Skipping merchant webhook.",
                evt.EventId, evt.PaymentId, evt.Outcome);
            return false;
        }

        var result = await dispatcher.DispatchAsync(evt, cancellationToken);

        // AC-4: If delivery exhausted, route to dead-letter queue without failing payment state
        if (!result.Success && !string.IsNullOrWhiteSpace(result.EndpointUrl) && context is not null)
        {
            logger.LogWarning(
                "Merchant webhook delivery exhausted for event {EventId} (payment {PaymentId}). Routing to dead-letter queue.",
                evt.EventId, evt.PaymentId);

            var deadLetter = new MerchantWebhookDeadLetterMessage(
                EventId: evt.EventId,
                PaymentId: evt.PaymentId,
                PartyId: evt.PartyId,
                EndpointUrl: result.EndpointUrl,
                PayloadJson: result.PayloadJson,
                DeliveryAttempts: result.DeliveryAttempts,
                LastErrorMessage: result.ErrorMessage ?? "Delivery attempts exhausted.",
                FailedAt: _timeProvider.GetUtcNow());

            try
            {
                var sendEndpoint = await context.GetSendEndpoint(new Uri("queue:merchant-webhook-dlq"));
                await sendEndpoint.Send(deadLetter, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed direct send to queue:merchant-webhook-dlq for event {EventId}; falling back to publish.",
                    evt.EventId);
                await context.Publish(deadLetter, cancellationToken);
            }
        }

        return result.Success;
    }

    public static bool IsTerminalState(string? outcome) =>
        !string.IsNullOrWhiteSpace(outcome) && TerminalOutcomes.Contains(outcome);
}
