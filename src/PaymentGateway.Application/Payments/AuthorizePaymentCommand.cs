using MediatR;

namespace PaymentGateway.Application.Payments;

public record AuthorizePaymentCommand(
    string PartyId,
    long Amount,
    string Currency,
    string? SettlementChannel = null) : IRequest<PaymentDto>;
