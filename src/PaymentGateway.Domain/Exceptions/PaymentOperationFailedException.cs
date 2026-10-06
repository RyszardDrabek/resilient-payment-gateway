namespace PaymentGateway.Domain.Exceptions;

public sealed class PaymentOperationFailedException(string operation, string reason)
    : Exception($"Payment operation '{operation}' failed: {reason}")
{
    public string Operation { get; } = operation;
    public string Reason { get; } = reason;
}
