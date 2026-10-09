using Microsoft.Extensions.Options;
using PaymentGateway.Application.Events;
using PaymentGateway.Application.Risk.Models;
using PaymentGateway.Application.Risk.Ports;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;

namespace PaymentGateway.Infrastructure.Risk;

public sealed class DeterministicRiskScorer(IOptions<RiskOptions> options) : IRiskScorer
{
    private readonly RiskOptions _options = options.Value;

    public float[] ComputeFeatureVector(PaymentLifecycleEvent lifecycleEvent)
    {
        float logAmount = MathF.Log(Math.Max(1f, (float)lifecycleEvent.Amount));
        float currencyCode = (Math.Abs(lifecycleEvent.Currency.ToUpperInvariant().GetHashCode() % 100)) / 100f;
        float version = Math.Clamp((float)lifecycleEvent.Version / 10f, 0f, 1f);
        float outcomeWeight = lifecycleEvent.Outcome.Equals("Declined", StringComparison.OrdinalIgnoreCase) ? 0.0f : 1.0f;

        return [logAmount, currencyCode, version, outcomeWeight];
    }

    public Task<RiskScoringResult> ScoreAsync(
        PaymentLifecycleEvent lifecycleEvent,
        IReadOnlyList<PartyRiskContext> historicalContexts,
        IReadOnlyList<PartyRiskContext> similarContexts,
        CancellationToken ct = default)
    {
        var vector = ComputeFeatureVector(lifecycleEvent);

        // Check if party has historical transactions
        if (historicalContexts.Count > 0)
        {
            double averageAmount = historicalContexts.Average(h => (double)h.Amount);
            double ratio = (double)lifecycleEvent.Amount / Math.Max(1.0, averageAmount);

            if (ratio >= _options.AnomalyAmountMultiplier && lifecycleEvent.Amount > 1000)
            {
                return Task.FromResult(new RiskScoringResult(
                    RiskVerdictStatus.Anomalous,
                    $"Transaction amount ({lifecycleEvent.Amount} {lifecycleEvent.Currency}) is {ratio:F1}x the historical average ({averageAmount:F0}) across {historicalContexts.Count} prior events.",
                    IsAnomalous: true,
                    FeatureVector: vector));
            }
        }
        else
        {
            // Brand new party with huge transaction
            if (lifecycleEvent.Amount >= _options.NewPartyAnomalyAmountThreshold)
            {
                return Task.FromResult(new RiskScoringResult(
                    RiskVerdictStatus.Anomalous,
                    $"New party first transaction amount ({lifecycleEvent.Amount} {lifecycleEvent.Currency}) exceeds new party risk threshold ({_options.NewPartyAnomalyAmountThreshold}).",
                    IsAnomalous: true,
                    FeatureVector: vector));
            }
        }

        return Task.FromResult(new RiskScoringResult(
            RiskVerdictStatus.Clean,
            $"Normal transaction activity within party risk limits (history: {historicalContexts.Count} events, similar contexts: {similarContexts.Count}).",
            IsAnomalous: false,
            FeatureVector: vector));
    }
}
