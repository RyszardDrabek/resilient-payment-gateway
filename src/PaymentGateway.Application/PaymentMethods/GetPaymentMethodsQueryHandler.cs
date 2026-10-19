using MediatR;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Application.PaymentMethods;

public sealed class GetPaymentMethodsQueryHandler(
    IPaymentMethodRepository repository) : IRequestHandler<GetPaymentMethodsQuery, IReadOnlyList<PaymentMethodDto>>
{
    public async Task<IReadOnlyList<PaymentMethodDto>> Handle(GetPaymentMethodsQuery request, CancellationToken cancellationToken)
    {
        var items = await repository.ListByPartyIdAsync(request.PartyId, request.ActiveOnly, cancellationToken);

        return items.Select(m => new PaymentMethodDto(
            m.Id,
            m.PartyId,
            m.SettlementChannel,
            m.CardBrand,
            m.MaskedPan,
            m.ExpiryMonth,
            m.ExpiryYear,
            m.Status.ToString(),
            m.CreatedAt,
            m.RevokedAt)).ToList();
    }
}
