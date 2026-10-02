namespace PaymentGateway.Domain.Events;

public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}
