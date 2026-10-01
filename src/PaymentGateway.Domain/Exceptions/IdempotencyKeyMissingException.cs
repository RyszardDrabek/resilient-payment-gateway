namespace PaymentGateway.Domain.Exceptions;

public sealed class IdempotencyKeyMissingException : Exception
{
    public IdempotencyKeyMissingException(string message = "Idempotency key is required on payment commands.")
        : base(message)
    {
    }
}
