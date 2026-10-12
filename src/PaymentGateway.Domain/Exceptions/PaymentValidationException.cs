namespace PaymentGateway.Domain.Exceptions;

public sealed class PaymentValidationException(string message) : Exception(message);
