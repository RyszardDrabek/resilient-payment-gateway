namespace PaymentGateway.Api.Endpoints;

public sealed record SubmitThreeDsResultRequest(
    string? ThreeDsResult = null,
    IReadOnlyDictionary<string, string>? Details = null);
