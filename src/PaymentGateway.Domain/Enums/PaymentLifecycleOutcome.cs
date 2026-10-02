namespace PaymentGateway.Domain.Enums;

public enum PaymentLifecycleOutcome
{
    Authorized,
    Declined,
    Captured,
    Cancelled,
    Refunded,
    Pending,
    Unknown
}
