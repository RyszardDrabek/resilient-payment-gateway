using MediatR;

namespace PaymentGateway.Application.Payments;

public record GetPaymentByIdQuery(string PaymentId) : IRequest<PaymentDto?>;
