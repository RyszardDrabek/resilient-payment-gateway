namespace PaymentGateway.Application.Risk.Models;

public enum InlineRiskDecision
{
    Allow,
    Challenge3Ds,
    Block
}

public sealed record InlineRiskEvaluation(
    InlineRiskDecision Decision,
    int RiskScore,
    string Reason,
    bool IsDegraded = false,
    long EvaluationDurationMs = 0)
{
    public static InlineRiskEvaluation Allow(int score, string reason, long durationMs = 0) =>
        new(InlineRiskDecision.Allow, score, reason, false, durationMs);

    public static InlineRiskEvaluation Challenge3Ds(int score, string reason, long durationMs = 0) =>
        new(InlineRiskDecision.Challenge3Ds, score, reason, false, durationMs);

    public static InlineRiskEvaluation Block(int score, string reason, long durationMs = 0) =>
        new(InlineRiskDecision.Block, score, reason, false, durationMs);

    public static InlineRiskEvaluation FailOpen(string reason, long durationMs = 0) =>
        new(InlineRiskDecision.Allow, 0, $"Fail-open: {reason}", true, durationMs);
}
