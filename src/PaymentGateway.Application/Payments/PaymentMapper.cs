using PaymentGateway.Domain.Entities;
using Riok.Mapperly.Abstractions;

namespace PaymentGateway.Application.Payments;

[Mapper]
public static partial class PaymentMapper
{
    [MapProperty(nameof(Payment.Id), nameof(PaymentDto.PaymentId))]
    [MapProperty(nameof(Payment.ThreeDsAction), nameof(PaymentDto.Action))]
    [MapperIgnoreSource(nameof(Payment.Version))]
    [MapperIgnoreSource(nameof(Payment.DomainEvents))]
    public static partial PaymentDto ToDto(Payment payment);

    public static partial ThreeDsActionDto ToDto(ThreeDsActionDetails action);
}
