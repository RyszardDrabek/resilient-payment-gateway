namespace PaymentGateway.Api.Endpoints;

public sealed record DispositionRiskFlagRequest(string? Disposition, string? Notes);
