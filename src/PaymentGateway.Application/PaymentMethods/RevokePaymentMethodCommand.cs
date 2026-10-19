using MediatR;

namespace PaymentGateway.Application.PaymentMethods;

public record RevokePaymentMethodCommand(string Id) : IRequest<bool>;
