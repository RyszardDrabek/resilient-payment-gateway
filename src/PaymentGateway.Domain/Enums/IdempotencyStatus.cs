namespace PaymentGateway.Domain.Enums;

public enum IdempotencyStatus
{
    InFlight,
    Completed,
    Failed
}
