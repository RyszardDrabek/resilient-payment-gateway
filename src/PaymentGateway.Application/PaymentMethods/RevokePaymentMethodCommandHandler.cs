using MediatR;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Application.PaymentMethods;

public sealed class RevokePaymentMethodCommandHandler(
    IPaymentMethodRepository repository) : IRequestHandler<RevokePaymentMethodCommand, bool>
{
    public async Task<bool> Handle(RevokePaymentMethodCommand request, CancellationToken cancellationToken)
    {
        var paymentMethod = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (paymentMethod is null)
        {
            return false;
        }

        paymentMethod.Revoke();
        await repository.SaveAsync(paymentMethod, cancellationToken);
        return true;
    }
}
