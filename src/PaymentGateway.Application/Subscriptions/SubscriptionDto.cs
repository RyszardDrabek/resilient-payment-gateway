namespace PaymentGateway.Application.Subscriptions;

public record SubscriptionDto(
    string Id,
    string PartyId,
    string PaymentMethodId,
    long Amount,
    string Currency,
    string Interval,
    string State,
    DateTimeOffset NextBillingAt,
    int FailureCount,
    DateTimeOffset? LastBilledAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CancelledAt);
