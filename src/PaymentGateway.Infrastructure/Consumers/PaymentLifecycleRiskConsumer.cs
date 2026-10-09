using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.Events;
using PaymentGateway.Application.Risk.Commands;
using PaymentGateway.Application.Risk.Ports;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Infrastructure.Risk;

namespace PaymentGateway.Infrastructure.Consumers;

public sealed class PaymentLifecycleRiskConsumer(
    ISender mediator,
    IRiskVerdictRepository verdictRepository,
    IOptions<RiskOptions> options,
    ILogger<PaymentLifecycleRiskConsumer> logger)
    : IConsumer<PaymentLifecycleEvent>
{
    public Task Consume(ConsumeContext<PaymentLifecycleEvent> context) =>
        ProcessEventAsync(context.Message, context.CancellationToken);

    public async Task<RiskVerdict> ProcessEventAsync(PaymentLifecycleEvent evt, CancellationToken cancellationToken = default)
    {
        var timeout = TimeSpan.FromSeconds(options.Value.ScoreTimeoutSeconds);

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            logger.LogInformation("Scoring risk off-path for payment {PaymentId}, event {EventId}", evt.PaymentId, evt.EventId);
            return await mediator.Send(new ScorePaymentLifecycleEventCommand(evt), linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            // AC-5: Time budget exceeded -> fail open, record degradation
            logger.LogWarning("Risk scoring timed out after {Timeout}s for event {EventId} (payment {PaymentId}). Recording degradation.",
                options.Value.ScoreTimeoutSeconds, evt.EventId, evt.PaymentId);

            var degraded = RiskVerdict.Degraded(
                Guid.NewGuid().ToString(),
                evt.EventId,
                evt.PaymentId,
                evt.PartyId,
                $"Scoring time budget of {options.Value.ScoreTimeoutSeconds}s exceeded",
                DateTimeOffset.UtcNow);

            try
            {
                await verdictRepository.SaveAsync(degraded, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to persist timeout-degraded verdict for event {EventId}", evt.EventId);
            }

            return degraded;
        }
        catch (Exception ex)
        {
            // AC-5 / Fail-open: Never throw unhandled to let broker back up payment path
            logger.LogWarning(ex, "Risk scoring consumer encountered unexpected error for event {EventId}: {Message}", evt.EventId, ex.Message);

            var degraded = RiskVerdict.Degraded(
                Guid.NewGuid().ToString(),
                evt.EventId,
                evt.PaymentId,
                evt.PartyId,
                $"Scoring error: {ex.Message}",
                DateTimeOffset.UtcNow);

            try
            {
                await verdictRepository.SaveAsync(degraded, CancellationToken.None);
            }
            catch (Exception persistEx)
            {
                logger.LogError(persistEx, "Failed to persist error-degraded verdict for event {EventId}", evt.EventId);
            }

            return degraded;
        }
    }
}
