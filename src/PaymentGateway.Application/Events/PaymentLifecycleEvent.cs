namespace PaymentGateway.Application.Events;

public sealed record PaymentLifecycleEvent(
    string EventId,
    string PaymentId,
    string PartyId,
    string Outcome,
    long Amount,
    string Currency,
    int Version,
    DateTimeOffset OccurredAt);
