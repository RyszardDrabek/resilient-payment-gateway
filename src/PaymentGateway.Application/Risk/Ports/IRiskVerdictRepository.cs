using PaymentGateway.Domain.Entities;

namespace PaymentGateway.Application.Risk.Ports;

public interface IRiskVerdictRepository
{
    Task<RiskVerdict?> GetByEventIdAsync(string eventId, CancellationToken ct = default);
    Task<RiskVerdict?> GetByPaymentIdAsync(string paymentId, CancellationToken ct = default);
    Task<RiskVerdict?> GetByIdentifierAsync(string identifier, CancellationToken ct = default);
    Task SaveAsync(RiskVerdict verdict, CancellationToken ct = default);
}
