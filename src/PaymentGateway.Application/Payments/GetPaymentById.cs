using MediatR;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Application.Payments;

public record GetPaymentByIdQuery(string PaymentId) : IRequest<PaymentDto?>;

public sealed class GetPaymentByIdQueryHandler(IPaymentRepository repository)
    : IRequestHandler<GetPaymentByIdQuery, PaymentDto?>
{
    public async Task<PaymentDto?> Handle(GetPaymentByIdQuery request, CancellationToken ct)
    {
        var payment = await repository.GetByIdAsync(request.PaymentId, ct);
        return payment is null ? null : PaymentDto.FromDomain(payment);
    }
}
