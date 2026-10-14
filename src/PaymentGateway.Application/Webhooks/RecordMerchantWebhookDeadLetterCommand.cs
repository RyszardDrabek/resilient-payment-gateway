using MediatR;

namespace PaymentGateway.Application.Webhooks;

public sealed record RecordMerchantWebhookDeadLetterCommand(
    string EventId,
    string PaymentId,
    string MerchantId,
    string DestinationUrl,
    string PayloadJson,
    int DeliveryAttempts,
    string LastErrorMessage,
    int? LastStatusCode = null) : IRequest<Guid>;
