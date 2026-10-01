namespace PaymentGateway.Domain.Exceptions;

public sealed class IdempotencyConflictException : Exception
{
    public IdempotencyConflictException(string message = "Idempotency key has already been used with a materially different request.")
        : base(message)
    {
    }
}
