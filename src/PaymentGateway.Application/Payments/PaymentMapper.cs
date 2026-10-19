using PaymentGateway.Domain.Entities;
using Riok.Mapperly.Abstractions;

namespace PaymentGateway.Application.Payments;

[Mapper]
public static partial class PaymentMapper
{
    [MapProperty(nameof(Payment.Id), nameof(PaymentDto.PaymentId))]
    [MapProperty(nameof(Payment.ThreeDsAction), nameof(PaymentDto.Action))]
    [MapProperty(nameof(Payment.TransactionOrigin), nameof(PaymentDto.TransactionOrigin))]
    [MapperIgnoreSource(nameof(Payment.Version))]
    [MapperIgnoreSource(nameof(Payment.DomainEvents))]
    public static partial PaymentDto ToDto(Payment payment);

    public static partial ThreeDsActionDto ToDto(ThreeDsActionDetails action);

    private static string MapOrigin(TransactionOrigin origin) => origin switch
    {
        TransactionOrigin.Mit => "MIT",
        _ => "CIT"
    };
}
