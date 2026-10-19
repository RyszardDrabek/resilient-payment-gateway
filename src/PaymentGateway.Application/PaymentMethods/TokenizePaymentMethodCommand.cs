using MediatR;

namespace PaymentGateway.Application.PaymentMethods;

public record TokenizePaymentMethodCommand(
    string PartyId,
    string SettlementChannel,
    string CardBrand,
    string MaskedPan,
    int ExpiryMonth,
    int ExpiryYear,
    string Token) : IRequest<PaymentMethodDto>;
