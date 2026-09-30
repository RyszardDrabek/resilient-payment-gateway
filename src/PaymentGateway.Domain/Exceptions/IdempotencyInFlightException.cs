namespace PaymentGateway.Domain.Exceptions;

public sealed class IdempotencyInFlightException : Exception
{
    public IdempotencyInFlightException(string message = "A command with this idempotency key is currently executing.")
        : base(message)
    {
    }
}
