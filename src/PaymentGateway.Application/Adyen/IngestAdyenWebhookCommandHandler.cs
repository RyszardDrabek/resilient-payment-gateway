using MediatR;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Application.Adyen;

public sealed class IngestAdyenWebhookCommandHandler(
    IAdyenHmacValidator hmacValidator,
    IAdyenNotificationRepository notificationRepository,
    IPaymentRepository paymentRepository,
    IAdyenReconciliationService? reconciliationService = null) : IRequestHandler<IngestAdyenWebhookCommand, AdyenWebhookIngestionResult>
{
    public async Task<AdyenWebhookIngestionResult> Handle(IngestAdyenWebhookCommand request, CancellationToken cancellationToken)
    {
        if (request.Payload.NotificationItems is null || request.Payload.NotificationItems.Count == 0)
        {
            return AdyenWebhookIngestionResult.Failure("Payload contains no notification items.");
        }

        var savedNotifications = new List<AdyenNotification>();

        foreach (var wrapper in request.Payload.NotificationItems)
        {
            var item = wrapper.NotificationRequestItem;
            if (item is null)
            {
                continue;
            }

            var isValidHmac = hmacValidator.Validate(item);
            if (!isValidHmac)
            {
                return AdyenWebhookIngestionResult.InvalidHmac();
            }

            var isReplay = await notificationRepository.ExistsAsync(item.PspReference, item.EventCode, cancellationToken);
            if (isReplay)
            {
                return AdyenWebhookIngestionResult.Replay($"Notification with PspReference '{item.PspReference}' and EventCode '{item.EventCode}' has already been processed.");
            }

            string? correlatedPaymentId = null;

            if (!string.IsNullOrWhiteSpace(item.MerchantReference))
            {
                var paymentByRef = await paymentRepository.GetByIdAsync(item.MerchantReference, cancellationToken);
                if (paymentByRef is not null)
                {
                    correlatedPaymentId = paymentByRef.Id;
                }
            }

            if (correlatedPaymentId is null && !string.IsNullOrWhiteSpace(item.PspReference))
            {
                var paymentByPsp = await paymentRepository.GetByChannelReferenceAsync(item.PspReference, cancellationToken);
                if (paymentByPsp is not null)
                {
                    correlatedPaymentId = paymentByPsp.Id;
                }
            }

            if (correlatedPaymentId is null && !string.IsNullOrWhiteSpace(item.OriginalReference))
            {
                var paymentByOrig = await paymentRepository.GetByChannelReferenceAsync(item.OriginalReference, cancellationToken);
                if (paymentByOrig is not null)
                {
                    correlatedPaymentId = paymentByOrig.Id;
                }
            }

            var notification = AdyenNotification.Create(
                pspReference: item.PspReference,
                originalReference: item.OriginalReference,
                merchantAccountCode: item.MerchantAccountCode,
                merchantReference: item.MerchantReference,
                eventCode: item.EventCode,
                eventDate: item.EventDate,
                amountValue: item.Amount?.Value ?? 0,
                amountCurrency: item.Amount?.Currency ?? string.Empty,
                success: string.Equals(item.Success, "true", StringComparison.OrdinalIgnoreCase),
                reason: item.Reason,
                correlatedPaymentId: correlatedPaymentId);

            await notificationRepository.AddAsync(notification, cancellationToken);
            savedNotifications.Add(notification);
        }

        await notificationRepository.SaveChangesAsync(cancellationToken);

        if (reconciliationService is not null)
        {
            foreach (var notification in savedNotifications)
            {
                await reconciliationService.ReconcileNotificationAsync(notification.Id, cancellationToken);
            }
        }

        return AdyenWebhookIngestionResult.Success();
    }
}

