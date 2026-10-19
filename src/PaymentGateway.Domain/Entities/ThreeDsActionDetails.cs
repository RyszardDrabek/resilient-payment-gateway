namespace PaymentGateway.Domain.Entities;

/// <summary>
/// Immutable action parameters required for 3DS2 / Strong Customer Authentication (SCA) challenge (ADR-019).
/// </summary>
public sealed record ThreeDsActionDetails(
    string ActionType,
    string? TargetUrl = null,
    string? Token = null,
    DateTimeOffset? ExpiresAt = null
);
