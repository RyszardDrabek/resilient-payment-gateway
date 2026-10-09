using PaymentGateway.Application.Events;
using PaymentGateway.Application.Risk.Models;
using PaymentGateway.Domain.Entities;

namespace PaymentGateway.Application.Risk.Ports;

public interface IRiskScorer
{
    float[] ComputeFeatureVector(PaymentLifecycleEvent lifecycleEvent);
    Task<RiskScoringResult> ScoreAsync(
        PaymentLifecycleEvent lifecycleEvent,
        IReadOnlyList<PartyRiskContext> historicalContexts,
        IReadOnlyList<PartyRiskContext> similarContexts,
        CancellationToken ct = default);
}
