namespace PaymentGateway.Domain.Entities;

public enum PaymentState
{
    Authorized,
    Declined,
    Pending,
    Unknown,
    Captured,
    Cancelled,
    Refunded
}
