using MediatR;
using PaymentGateway.Application.Payments;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Application.Adyen;

public sealed class AdyenReconciliationService(
    IAdyenNotificationRepository notificationRepository,
    IPaymentRepository paymentRepository,
    ISettlementPort settlementPort,
    IMediator mediator) : IAdyenReconciliationService
{
    public async Task<ReconciliationResult> ReconcileNotificationAsync(Guid notificationId, CancellationToken ct = default)
    {
        var notification = await notificationRepository.GetByIdAsync(notificationId, ct);
        if (notification is null)
        {
            return new ReconciliationResult(ReconciliationStatus.NotFound, Message: $"Notification '{notificationId}' not found.");
        }

        if (notification.Status == "Processed")
        {
            return new ReconciliationResult(
                ReconciliationStatus.AlreadyProcessed,
                PaymentId: notification.CorrelatedPaymentId,
                Message: "Notification has already been processed.");
        }

        // Try correlating if not already correlated
        if (string.IsNullOrWhiteSpace(notification.CorrelatedPaymentId))
        {
            Payment? correlatedPayment = null;

            if (!string.IsNullOrWhiteSpace(notification.MerchantReference))
            {
                correlatedPayment = await paymentRepository.GetByIdAsync(notification.MerchantReference, ct);
            }

            if (correlatedPayment is null && !string.IsNullOrWhiteSpace(notification.PspReference))
            {
                correlatedPayment = await paymentRepository.GetByChannelReferenceAsync(notification.PspReference, ct);
            }

            if (correlatedPayment is null && !string.IsNullOrWhiteSpace(notification.OriginalReference))
            {
                correlatedPayment = await paymentRepository.GetByChannelReferenceAsync(notification.OriginalReference, ct);
            }

            if (correlatedPayment is not null)
            {
                notification.MarkCorrelated(correlatedPayment.Id);
            }
            else
            {
                // AC-3: Retain for ops visibility, do NOT invent a payment
                notification.MarkUncorrelated();
                await notificationRepository.SaveChangesAsync(ct);
                return new ReconciliationResult(
                    ReconciliationStatus.Uncorrelated,
                    Message: "Notification could not be correlated to any payment and has been retained for ops visibility.");
            }
        }

        var outcome = DetermineOutcome(notification.EventCode, notification.Success);
        var command = new ApplyAcquirerOutcomeCommand(
            PaymentId: notification.CorrelatedPaymentId!,
            Outcome: outcome,
            ChannelReference: notification.PspReference,
            Reason: notification.Reason);

        var outcomeResult = await mediator.Send(command, ct);

        notification.MarkProcessed();
        await notificationRepository.SaveChangesAsync(ct);

        return new ReconciliationResult(
            ReconciliationStatus.Resolved,
            PaymentId: notification.CorrelatedPaymentId,
            Outcome: outcome.ToString(),
            Message: outcomeResult.Message);
    }

    public async Task<ReconciliationResult> ReconcileUnresolvedPaymentAsync(
        string paymentId,
        TimeSpan? reconciliationWindow = null,
        CancellationToken ct = default)
    {
        var payment = await paymentRepository.GetByIdAsync(paymentId, ct);
        if (payment is null)
        {
            return new ReconciliationResult(ReconciliationStatus.NotFound, Message: $"Payment '{paymentId}' not found.");
        }

        if (payment.State != PaymentState.Pending && payment.State != PaymentState.Unknown)
        {
            return new ReconciliationResult(
                ReconciliationStatus.AlreadyProcessed,
                PaymentId: payment.Id,
                Outcome: payment.State.ToString(),
                Message: $"Payment is already resolved in state '{payment.State}'.");
        }

        // AC-4: Query Adyen for payment status using retained references
        var statusResult = await settlementPort.QueryPaymentStatusAsync(
            paymentId: payment.Id,
            channelReference: payment.ChannelReference,
            merchantReference: payment.Id,
            channel: payment.SettlementChannel,
            ct: ct);

        if (statusResult.IsAuthorized)
        {
            var applyCommand = new ApplyAcquirerOutcomeCommand(
                PaymentId: payment.Id,
                Outcome: PaymentLifecycleOutcome.Authorized,
                ChannelReference: !string.IsNullOrWhiteSpace(statusResult.ChannelReference) ? statusResult.ChannelReference : payment.ChannelReference);

            await mediator.Send(applyCommand, ct);

            return new ReconciliationResult(
                ReconciliationStatus.Resolved,
                PaymentId: payment.Id,
                Outcome: PaymentLifecycleOutcome.Authorized.ToString(),
                Message: "Payment successfully resolved to Authorized via active Adyen status query.");
        }

        if (!statusResult.IsSuccessful && !statusResult.IsUnanswered && !string.IsNullOrWhiteSpace(statusResult.DeclineReason))
        {
            var applyCommand = new ApplyAcquirerOutcomeCommand(
                PaymentId: payment.Id,
                Outcome: PaymentLifecycleOutcome.Declined,
                ChannelReference: !string.IsNullOrWhiteSpace(statusResult.ChannelReference) ? statusResult.ChannelReference : payment.ChannelReference,
                Reason: statusResult.DeclineReason);

            await mediator.Send(applyCommand, ct);

            return new ReconciliationResult(
                ReconciliationStatus.Resolved,
                PaymentId: payment.Id,
                Outcome: PaymentLifecycleOutcome.Declined.ToString(),
                Message: $"Payment resolved to Declined via active Adyen status query ({statusResult.DeclineReason}).");
        }

        // Still unresolved after query -> exposed for ops visibility (AC-4)
        return new ReconciliationResult(
            ReconciliationStatus.Unresolved,
            PaymentId: payment.Id,
            Outcome: payment.State.ToString(),
            Message: "Payment remains unresolved after Adyen status query and is exposed for ops visibility.");
    }

    public async Task<IReadOnlyList<AdyenNotification>> GetUncorrelatedNotificationsAsync(CancellationToken ct = default)
    {
        return await notificationRepository.GetUncorrelatedAsync(ct);
    }

    public async Task<IReadOnlyList<Payment>> GetUnresolvedPaymentsAsync(CancellationToken ct = default)
    {
        return await paymentRepository.GetUnresolvedAsync(ct);
    }

    public async Task<IReadOnlyList<ReconciliationResult>> ReconcileAllPendingAsync(CancellationToken ct = default)
    {
        var results = new List<ReconciliationResult>();

        var unprocessedNotifications = await notificationRepository.GetUnprocessedCorrelatedAsync(ct);
        foreach (var notification in unprocessedNotifications)
        {
            var result = await ReconcileNotificationAsync(notification.Id, ct);
            results.Add(result);
        }

        var unresolvedPayments = await paymentRepository.GetUnresolvedAsync(ct);
        foreach (var payment in unresolvedPayments)
        {
            var result = await ReconcileUnresolvedPaymentAsync(payment.Id, null, ct);
            results.Add(result);
        }

        return results;
    }

    private static PaymentLifecycleOutcome DetermineOutcome(string eventCode, bool success)
    {
        return eventCode.ToUpperInvariant() switch
        {
            "AUTHORISATION" => success ? PaymentLifecycleOutcome.Authorized : PaymentLifecycleOutcome.Declined,
            "CAPTURE" => success ? PaymentLifecycleOutcome.Captured : PaymentLifecycleOutcome.Declined,
            "CANCELLATION" => PaymentLifecycleOutcome.Cancelled,
            "REFUND" => PaymentLifecycleOutcome.Refunded,
            "CANCEL_OR_REFUND" => PaymentLifecycleOutcome.Cancelled,
            _ => success ? PaymentLifecycleOutcome.Authorized : PaymentLifecycleOutcome.Declined
        };
    }
}
