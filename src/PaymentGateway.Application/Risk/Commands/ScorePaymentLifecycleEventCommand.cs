using MediatR;
using Microsoft.Extensions.Logging;
using PaymentGateway.Application.Events;
using PaymentGateway.Application.Risk.Ports;
using PaymentGateway.Domain.Entities;

namespace PaymentGateway.Application.Risk.Commands;

public sealed record ScorePaymentLifecycleEventCommand(PaymentLifecycleEvent Event) : IRequest<RiskVerdict>;

public sealed class ScorePaymentLifecycleEventCommandHandler(
    IRiskVerdictRepository verdictRepository,
    IRiskFlagRepository flagRepository,
    IRiskContextStore contextStore,
    IRiskScorer riskScorer,
    ILogger<ScorePaymentLifecycleEventCommandHandler> logger)
    : IRequestHandler<ScorePaymentLifecycleEventCommand, RiskVerdict>
{
    public async Task<RiskVerdict> Handle(ScorePaymentLifecycleEventCommand request, CancellationToken cancellationToken)
    {
        var evt = request.Event;

        // AC-1 / Deduplication: check if verdict already recorded for this event
        var existing = await verdictRepository.GetByEventIdAsync(evt.EventId, cancellationToken);
        if (existing is not null)
        {
            logger.LogInformation("Risk verdict already exists for event {EventId}", evt.EventId);
            return existing;
        }

        try
        {
            var featureVector = riskScorer.ComputeFeatureVector(evt);

            // AC-2: Retrieve party's historical risk context and similar prior context
            var history = await contextStore.GetPartyHistoryAsync(evt.PartyId, cancellationToken);
            var similar = await contextStore.FindSimilarContextsAsync(evt.PartyId, featureVector, 5, cancellationToken);

            var scoring = await riskScorer.ScoreAsync(evt, history, similar, cancellationToken);

            RiskVerdict verdict;
            if (scoring.IsAnomalous)
            {
                // AC-3: Create reviewable risk flag when anomalous
                var flag = new RiskFlag(
                    Guid.NewGuid().ToString(),
                    evt.PaymentId,
                    evt.PartyId,
                    evt.EventId,
                    scoring.Reason,
                    DateTimeOffset.UtcNow);

                await flagRepository.SaveAsync(flag, cancellationToken);

                verdict = RiskVerdict.Anomalous(
                    Guid.NewGuid().ToString(),
                    evt.EventId,
                    evt.PaymentId,
                    evt.PartyId,
                    scoring.Reason,
                    flag.Id,
                    DateTimeOffset.UtcNow);
            }
            else
            {
                verdict = RiskVerdict.Clean(
                    Guid.NewGuid().ToString(),
                    evt.EventId,
                    evt.PaymentId,
                    evt.PartyId,
                    scoring.Reason,
                    DateTimeOffset.UtcNow);
            }

            await verdictRepository.SaveAsync(verdict, cancellationToken);

            var newContext = new PartyRiskContext(
                Guid.NewGuid().ToString(),
                evt.PartyId,
                evt.PaymentId,
                evt.EventId,
                evt.Amount,
                evt.Currency,
                scoring.FeatureVector,
                DateTimeOffset.UtcNow);

            await contextStore.RecordContextAsync(newContext, cancellationToken);

            return verdict;
        }
        catch (Exception ex)
        {
            // AC-5: Fail-open and record explicit degradation
            logger.LogWarning(ex, "Risk scoring degraded for event {EventId} (payment {PaymentId}): {Message}", evt.EventId, evt.PaymentId, ex.Message);

            var degradedVerdict = RiskVerdict.Degraded(
                Guid.NewGuid().ToString(),
                evt.EventId,
                evt.PaymentId,
                evt.PartyId,
                $"Degraded due to: {ex.Message}",
                DateTimeOffset.UtcNow);

            try
            {
                await verdictRepository.SaveAsync(degradedVerdict, CancellationToken.None);
            }
            catch (Exception persistEx)
            {
                logger.LogError(persistEx, "Failed to persist degraded verdict for event {EventId}", evt.EventId);
            }

            return degradedVerdict;
        }
    }
}
