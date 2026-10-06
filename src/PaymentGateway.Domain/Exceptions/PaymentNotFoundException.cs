namespace PaymentGateway.Domain.Exceptions;

public sealed class PaymentNotFoundException(string paymentId)
    : Exception($"Payment with identifier '{paymentId}' was not found.")
{
    public string PaymentId { get; } = paymentId;
}
