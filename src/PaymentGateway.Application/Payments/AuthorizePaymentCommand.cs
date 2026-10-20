using MediatR;

namespace PaymentGateway.Application.Payments;

public record AuthorizePaymentCommand(
    string IdempotencyKey,
    string PartyId,
    long Amount,
    string Currency,
    string? SettlementChannel = null,
    string? TransactionOrigin = null,
    string? PaymentMethodId = null,
    string? SubscriptionId = null,
    bool StorePaymentMethod = false,
    string? QuoteId = null) : IRequest<PaymentDto>;
