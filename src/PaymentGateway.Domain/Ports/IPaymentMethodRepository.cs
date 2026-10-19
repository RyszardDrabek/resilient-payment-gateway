using PaymentGateway.Domain.Entities;

namespace PaymentGateway.Domain.Ports;

public interface IPaymentMethodRepository
{
    Task<PaymentMethodToken?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PaymentMethodToken>> ListByPartyIdAsync(string partyId, bool activeOnly = true, CancellationToken cancellationToken = default);
    Task SaveAsync(PaymentMethodToken paymentMethod, CancellationToken cancellationToken = default);
}
