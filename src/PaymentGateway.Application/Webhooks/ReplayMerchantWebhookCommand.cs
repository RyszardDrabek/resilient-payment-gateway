using MediatR;

namespace PaymentGateway.Application.Webhooks;

public sealed record ReplayMerchantWebhookCommand(
    string MerchantId,
    Guid DlqEntryId) : IRequest<ReplayMerchantWebhookResult?>;
