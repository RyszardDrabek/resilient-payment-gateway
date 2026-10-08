using Microsoft.EntityFrameworkCore;
using PaymentGateway.Application.Adyen;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Infrastructure.Persistence;

namespace PaymentGateway.Infrastructure.Repositories;

public sealed class AdyenNotificationRepository(PaymentDbContext dbContext) : IAdyenNotificationRepository
{
    public async Task<bool> ExistsAsync(string pspReference, string eventCode, CancellationToken ct)
    {
        return await dbContext.AdyenNotifications
            .AnyAsync(n => n.PspReference == pspReference && n.EventCode == eventCode, ct);
    }

    public async Task AddAsync(AdyenNotification notification, CancellationToken ct)
    {
        await dbContext.AdyenNotifications.AddAsync(notification, ct);
    }

    public async Task<AdyenNotification?> GetByPspReferenceAsync(string pspReference, CancellationToken ct)
    {
        return await dbContext.AdyenNotifications
            .FirstOrDefaultAsync(n => n.PspReference == pspReference, ct);
    }

    public async Task<AdyenNotification?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return await dbContext.AdyenNotifications
            .FirstOrDefaultAsync(n => n.Id == id, ct);
    }

    public async Task<IReadOnlyList<AdyenNotification>> GetByMerchantReferenceAsync(string merchantReference, CancellationToken ct)
    {
        return await dbContext.AdyenNotifications
            .Where(n => n.MerchantReference == merchantReference)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AdyenNotification>> GetUncorrelatedAsync(CancellationToken ct)
    {
        return await dbContext.AdyenNotifications
            .Where(n => n.CorrelatedPaymentId == null || n.Status == "Uncorrelated")
            .OrderBy(n => n.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AdyenNotification>> GetUnprocessedCorrelatedAsync(CancellationToken ct)
    {
        return await dbContext.AdyenNotifications
            .Where(n => n.CorrelatedPaymentId != null && n.Status != "Processed")
            .OrderBy(n => n.CreatedAt)
            .ToListAsync(ct);
    }


    public async Task SaveChangesAsync(CancellationToken ct)
    {
        await dbContext.SaveChangesAsync(ct);
    }
}
