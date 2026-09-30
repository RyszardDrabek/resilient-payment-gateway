using MediatR;

namespace PaymentGateway.Application.Payments;

public record AuthorizePaymentCommand(
    string IdempotencyKey,
    string PartyId,
    long Amount,
    string Currency,
    string? SettlementChannel = null) : IRequest<PaymentDto>;
