using PaymentGateway.Domain.Entities;

namespace PaymentGateway.Application.Risk.Ports;

public interface IRiskFlagRepository
{
    Task<RiskFlag?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<RiskFlag>> GetByPaymentIdAsync(string paymentId, CancellationToken ct = default);
    Task<(IReadOnlyList<RiskFlag> Items, int TotalCount)> GetOpenFlagsAsync(int page = 1, int pageSize = 50, CancellationToken ct = default);
    Task SaveAsync(RiskFlag flag, CancellationToken ct = default);
}
