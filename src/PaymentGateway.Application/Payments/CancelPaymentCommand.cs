using MediatR;

namespace PaymentGateway.Application.Payments;

public sealed record CancelPaymentCommand(
    string PaymentId,
    string IdempotencyKey) : IRequest<PaymentDto>;
