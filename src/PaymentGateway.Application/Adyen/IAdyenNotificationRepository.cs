using PaymentGateway.Domain.Entities;

namespace PaymentGateway.Application.Adyen;

public interface IAdyenNotificationRepository
{
    Task<bool> ExistsAsync(string pspReference, string eventCode, CancellationToken ct);
    Task AddAsync(AdyenNotification notification, CancellationToken ct);
    Task<AdyenNotification?> GetByPspReferenceAsync(string pspReference, CancellationToken ct);
    Task<IReadOnlyList<AdyenNotification>> GetByMerchantReferenceAsync(string merchantReference, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
