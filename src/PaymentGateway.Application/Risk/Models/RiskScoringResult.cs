using PaymentGateway.Domain.Enums;

namespace PaymentGateway.Application.Risk.Models;

public sealed record RiskScoringResult(
    RiskVerdictStatus Status,
    string Reason,
    bool IsAnomalous,
    float[] FeatureVector);
