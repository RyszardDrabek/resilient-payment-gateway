namespace PaymentGateway.Application.Payments;

/// <summary>
/// Data transfer object for 3DS2 challenge and authentication actions (ADR-019).
/// </summary>
public sealed record ThreeDsActionDto(
    string ActionType,
    string? TargetUrl = null,
    string? Token = null,
    DateTimeOffset? ExpiresAt = null);
