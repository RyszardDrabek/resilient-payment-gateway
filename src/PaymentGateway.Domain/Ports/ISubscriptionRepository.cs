using PaymentGateway.Domain.Entities;

namespace PaymentGateway.Domain.Ports;

public interface ISubscriptionRepository
{
    Task<Subscription?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Subscription>> ListByPartyIdAsync(string partyId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Subscription>> ListDueSubscriptionsAsync(DateTimeOffset asOf, int limit = 50, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Subscription>> ListAsync(int skip = 0, int take = 50, CancellationToken cancellationToken = default);
    Task SaveAsync(Subscription subscription, CancellationToken cancellationToken = default);
}
