namespace PaymentGateway.Edge.Problems;

/// <summary>
/// RFC 7807 problem document for payment-domain errors (ADR-007).
/// Never exposes stack traces, instrument values, or secrets in Detail.
/// </summary>
public sealed record PaymentProblemDetails(
    string Type,
    string Title,
    int Status,
    string Detail,
    string CorrelationId);
