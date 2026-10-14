using PaymentGateway.Domain.Entities;

namespace PaymentGateway.Domain.Ports;

public interface IMerchantWebhookDeadLetterRepository
{
    Task AddAsync(MerchantWebhookDeadLetter deadLetter, CancellationToken ct = default);
    Task<MerchantWebhookDeadLetter?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<MerchantWebhookDeadLetter>> ListByMerchantAsync(
        string merchantId,
        string? status = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default);
    Task UpdateAsync(MerchantWebhookDeadLetter deadLetter, CancellationToken ct = default);
}
