namespace PaymentGateway.Domain.Enums;

public enum PaymentLifecycleOutcome
{
    Authorized,
    Declined,
    Captured,
    PartiallyCaptured,
    Cancelled,
    Refunded,
    PartiallyRefunded,
    Pending,
    Unknown
}
