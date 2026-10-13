using PaymentGateway.Domain.Entities;

namespace PaymentGateway.Domain.Ports;

public interface IMerchantApiKeyRepository
{
    Task AddAsync(MerchantApiKey apiKey, CancellationToken ct = default);
    Task<IReadOnlyList<MerchantApiKey>> ListByMerchantAsync(string merchantId, CancellationToken ct = default);
    Task<MerchantApiKey?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<MerchantApiKey?> FindByPrefixAsync(string prefix, CancellationToken ct = default);
    Task UpdateAsync(MerchantApiKey apiKey, CancellationToken ct = default);
}
