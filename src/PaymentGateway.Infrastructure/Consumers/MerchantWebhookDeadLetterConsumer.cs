using MassTransit;
using Microsoft.Extensions.Logging;
using PaymentGateway.Application.Webhooks;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Infrastructure.Consumers;

public sealed class MerchantWebhookDeadLetterConsumer(
    IMerchantWebhookDeadLetterRepository repository,
    ILogger<MerchantWebhookDeadLetterConsumer> logger)
    : IConsumer<MerchantWebhookDeadLetterMessage>
{
    public async Task Consume(ConsumeContext<MerchantWebhookDeadLetterMessage> context)
    {
        var msg = context.Message;
        logger.LogWarning(
            "Persisting dead-letter webhook for event {EventId}, payment {PaymentId}, party {PartyId} (destination {EndpointUrl})",
            msg.EventId, msg.PaymentId, msg.PartyId, msg.EndpointUrl);

        var deadLetter = MerchantWebhookDeadLetter.Create(
            eventId: msg.EventId,
            paymentId: msg.PaymentId,
            merchantId: msg.PartyId,
            destinationUrl: msg.EndpointUrl,
            payloadJson: msg.PayloadJson,
            deliveryAttempts: msg.DeliveryAttempts,
            lastErrorMessage: msg.LastErrorMessage,
            failedAt: msg.FailedAt);

        await repository.AddAsync(deadLetter, context.CancellationToken);
    }
}
