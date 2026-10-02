namespace PaymentGateway.Edge.Problems;

/// <summary>
/// Canonical type URI constants for payment-domain RFC 7807 problem documents (ADR-007).
/// All values use the urn:rpg:problem: prefix, making them distinguishable from non-payment errors.
/// </summary>
public static class PaymentProblemTypes
{
    public const string Validation = "urn:rpg:problem:validation";
    public const string NotFound = "urn:rpg:problem:not-found";
    public const string Conflict = "urn:rpg:problem:conflict";
    public const string ServerError = "urn:rpg:problem:server-error";
    public const string RateLimit = "urn:rpg:problem:rate-limit";
}
