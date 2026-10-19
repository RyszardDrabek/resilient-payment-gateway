using MediatR;

namespace PaymentGateway.Application.Payments;

/// <summary>
/// Command to complete payment authorization following a 3DS2 challenge or redirect (ADR-019).
/// </summary>
public record SubmitThreeDsResultCommand(
    string PaymentId,
    string? ThreeDsResult = null,
    IReadOnlyDictionary<string, string>? Details = null
) : IRequest<PaymentDto>;
