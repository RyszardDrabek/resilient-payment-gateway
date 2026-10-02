using MassTransit;
using Microsoft.Extensions.Logging;
using PaymentGateway.Application.Events;

namespace PaymentGateway.Infrastructure.Events;

public sealed class PaymentLifecycleTestConsumer(
    ITestEventStore eventStore,
    ILogger<PaymentLifecycleTestConsumer> logger) : IConsumer<PaymentLifecycleEvent>
{
    public Task Consume(ConsumeContext<PaymentLifecycleEvent> context)
    {
        logger.LogInformation(
            "Phase-1 test consumer observed payment lifecycle event: {EventId} for {PaymentId}, outcome {Outcome}",
            context.Message.EventId,
            context.Message.PaymentId,
            context.Message.Outcome);

        eventStore.Record(context.Message);
        return Task.CompletedTask;
    }
}
