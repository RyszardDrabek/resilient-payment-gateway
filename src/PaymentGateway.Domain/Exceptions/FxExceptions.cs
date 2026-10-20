namespace PaymentGateway.Domain.Exceptions;

public class QuoteNotFoundException(string quoteId)
    : Exception($"Quote '{quoteId}' was not found.");

public class QuoteExpiredException(string quoteId, DateTimeOffset expiredAt)
    : Exception($"Quote '{quoteId}' expired at {expiredAt:O}.");

public class QuoteAlreadyConsumedException(string quoteId, string consumedByPaymentId)
    : Exception($"Quote '{quoteId}' was already consumed by payment '{consumedByPaymentId}'.");

public class QuoteMismatchException(string message)
    : Exception(message);
