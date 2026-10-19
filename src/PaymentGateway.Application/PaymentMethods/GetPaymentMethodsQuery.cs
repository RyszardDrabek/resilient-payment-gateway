using MediatR;

namespace PaymentGateway.Application.PaymentMethods;

public record GetPaymentMethodsQuery(string PartyId, bool ActiveOnly = true) : IRequest<IReadOnlyList<PaymentMethodDto>>;
