using MediatR;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Application.PaymentMethods;

public sealed class TokenizePaymentMethodCommandHandler(
    IPaymentMethodRepository repository) : IRequestHandler<TokenizePaymentMethodCommand, PaymentMethodDto>
{
    public async Task<PaymentMethodDto> Handle(TokenizePaymentMethodCommand request, CancellationToken cancellationToken)
    {
        var id = $"pm_{Guid.NewGuid():N}";
        var paymentMethod = new PaymentMethodToken(
            id,
            request.PartyId,
            request.SettlementChannel,
            request.CardBrand,
            request.MaskedPan,
            request.ExpiryMonth,
            request.ExpiryYear,
            request.Token);

        await repository.SaveAsync(paymentMethod, cancellationToken);

        return new PaymentMethodDto(
            paymentMethod.Id,
            paymentMethod.PartyId,
            paymentMethod.SettlementChannel,
            paymentMethod.CardBrand,
            paymentMethod.MaskedPan,
            paymentMethod.ExpiryMonth,
            paymentMethod.ExpiryYear,
            paymentMethod.Status.ToString(),
            paymentMethod.CreatedAt,
            paymentMethod.RevokedAt);
    }
}
