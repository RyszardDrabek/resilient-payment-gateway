using MediatR;

namespace PaymentGateway.Application.Adyen;

public sealed record IngestAdyenWebhookCommand(AdyenWebhookPayload Payload) : IRequest<AdyenWebhookIngestionResult>;
