using MediatR;

namespace PaymentGateway.Application.Payments;

public sealed record CapturePaymentCommand(
    string PaymentId,
    string IdempotencyKey) : IRequest<PaymentDto>;
