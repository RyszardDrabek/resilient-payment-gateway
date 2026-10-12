using PaymentGateway.Domain.Entities;

namespace PaymentGateway.Domain.Ports;

public interface IMerchantWebhookSubscriptionRepository
{
    Task<MerchantWebhookSubscription?> GetActiveByPartyIdAsync(string partyId, CancellationToken ct = default);
    Task AddAsync(MerchantWebhookSubscription subscription, CancellationToken ct = default);
}
