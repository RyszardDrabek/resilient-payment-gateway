using PaymentGateway.Domain.Entities;

namespace PaymentGateway.Domain.Ports;

public interface IPaymentRepository
{
    Task AddAsync(Payment payment, CancellationToken ct = default);
    Task UpdateAsync(Payment payment, CancellationToken ct = default);
    Task<Payment?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<Payment?> GetByChannelReferenceAsync(string channelReference, CancellationToken ct = default);
}
