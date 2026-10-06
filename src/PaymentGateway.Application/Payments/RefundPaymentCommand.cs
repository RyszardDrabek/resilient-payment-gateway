using MediatR;

namespace PaymentGateway.Application.Payments;

public sealed record RefundPaymentCommand(
    string PaymentId,
    string IdempotencyKey) : IRequest<PaymentDto>;
