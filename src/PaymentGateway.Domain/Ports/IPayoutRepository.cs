using PaymentGateway.Domain.Entities;

namespace PaymentGateway.Domain.Ports;

public interface IPayoutRepository
{
    Task<MerchantPayoutBatch?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<MerchantPayoutBatch>> ListByMerchantIdAsync(string merchantId, int skip = 0, int take = 50, CancellationToken ct = default);
    Task<HashSet<string>> GetPaidOutPaymentIdsAsync(IEnumerable<string> paymentIds, CancellationToken ct = default);
    Task SaveAsync(MerchantPayoutBatch batch, CancellationToken ct = default);
}
