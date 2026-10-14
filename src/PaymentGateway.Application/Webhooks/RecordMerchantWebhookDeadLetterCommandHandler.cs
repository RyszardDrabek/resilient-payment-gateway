using MediatR;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Application.Webhooks;

public sealed class RecordMerchantWebhookDeadLetterCommandHandler(IMerchantWebhookDeadLetterRepository repository)
    : IRequestHandler<RecordMerchantWebhookDeadLetterCommand, Guid>
{
    public async Task<Guid> Handle(
        RecordMerchantWebhookDeadLetterCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var record = MerchantWebhookDeadLetter.Create(
            command.EventId,
            command.PaymentId,
            command.MerchantId,
            command.DestinationUrl,
            command.PayloadJson,
            command.DeliveryAttempts,
            command.LastErrorMessage,
            command.LastStatusCode);

        await repository.AddAsync(record, ct);
        return record.Id;
    }
}
