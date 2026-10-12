using PaymentGateway.Application.Events;

namespace PaymentGateway.Application.Webhooks;

public interface IMerchantWebhookDispatcher
{
    Task<MerchantWebhookDispatchResult> DispatchAsync(PaymentLifecycleEvent lifecycleEvent, CancellationToken ct = default);
}
