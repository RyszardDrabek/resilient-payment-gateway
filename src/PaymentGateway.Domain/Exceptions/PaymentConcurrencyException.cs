namespace PaymentGateway.Domain.Exceptions;

public sealed class PaymentConcurrencyException(string paymentId)
    : Exception($"Concurrent modification detected for payment '{paymentId}'. The operation could not be completed.")
{
    public string PaymentId { get; } = paymentId;
}
