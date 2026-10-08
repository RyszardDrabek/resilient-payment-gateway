using PaymentGateway.Domain.Entities;

namespace PaymentGateway.Application.Adyen;

public interface IAdyenNotificationRepository
{
    Task<bool> ExistsAsync(string pspReference, string eventCode, CancellationToken ct);
    Task AddAsync(AdyenNotification notification, CancellationToken ct);
    Task<AdyenNotification?> GetByPspReferenceAsync(string pspReference, CancellationToken ct);
    Task<AdyenNotification?> GetByIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult<AdyenNotification?>(null);
    Task<IReadOnlyList<AdyenNotification>> GetByMerchantReferenceAsync(string merchantReference, CancellationToken ct);
    Task<IReadOnlyList<AdyenNotification>> GetUncorrelatedAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AdyenNotification>>([]);
    Task<IReadOnlyList<AdyenNotification>> GetUnprocessedCorrelatedAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AdyenNotification>>([]);
    Task SaveChangesAsync(CancellationToken ct);
}


