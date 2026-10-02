using PaymentGateway.Domain.Entities;
using Riok.Mapperly.Abstractions;

namespace PaymentGateway.Application.Payments;

[Mapper]
public static partial class PaymentMapper
{
    [MapProperty(nameof(Payment.Id), nameof(PaymentDto.PaymentId))]
    [MapperIgnoreSource(nameof(Payment.Version))]
    [MapperIgnoreSource(nameof(Payment.DomainEvents))]
    public static partial PaymentDto ToDto(Payment payment);
}
