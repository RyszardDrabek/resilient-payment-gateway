namespace PaymentGateway.Domain.Events;

/// <summary>
/// Defines an entity or aggregate root that raises in-memory domain events.
/// Interceptors and change trackers capture and persist these events (e.g. to transactional outbox)
/// before clearing them.
/// </summary>
public interface IHasDomainEvents
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    void ClearDomainEvents();
}
