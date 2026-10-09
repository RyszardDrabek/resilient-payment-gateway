using PaymentGateway.Domain.Entities;

namespace PaymentGateway.Application.Risk.Ports;

public interface IRiskFlagRepository
{
    Task<RiskFlag?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<RiskFlag>> GetByPaymentIdAsync(string paymentId, CancellationToken ct = default);
    Task SaveAsync(RiskFlag flag, CancellationToken ct = default);
}
