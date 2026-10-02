using PaymentGateway.Domain.Enums;

namespace PaymentGateway.Domain.Events;

public sealed record PaymentTransitionDomainEvent(
    string PaymentId,
    string PartyId,
    PaymentLifecycleOutcome Outcome,
    long Amount,
    string Currency,
    int Version,
    DateTimeOffset OccurredAt) : IDomainEvent;
