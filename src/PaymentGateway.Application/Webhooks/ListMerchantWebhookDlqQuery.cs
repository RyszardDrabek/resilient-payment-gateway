using MediatR;

namespace PaymentGateway.Application.Webhooks;

public sealed record ListMerchantWebhookDlqQuery(
    string MerchantId,
    string? Status = null,
    int Page = 1,
    int PageSize = 50) : IRequest<IReadOnlyList<MerchantWebhookDeadLetterDto>>;
