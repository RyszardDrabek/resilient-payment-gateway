using MediatR;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Application.Webhooks;

public sealed class ListMerchantWebhookDlqQueryHandler(IMerchantWebhookDeadLetterRepository repository)
    : IRequestHandler<ListMerchantWebhookDlqQuery, IReadOnlyList<MerchantWebhookDeadLetterDto>>
{
    public async Task<IReadOnlyList<MerchantWebhookDeadLetterDto>> Handle(
        ListMerchantWebhookDlqQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var list = await repository.ListByMerchantAsync(
            query.MerchantId,
            query.Status,
            query.Page,
            query.PageSize,
            ct);

        return list.Select(MapToDto).ToList();
    }

    private static MerchantWebhookDeadLetterDto MapToDto(MerchantWebhookDeadLetter r) =>
        new(
            r.Id,
            r.EventId,
            r.PaymentId,
            r.MerchantId,
            r.DestinationUrl,
            r.PayloadJson,
            r.DeliveryAttempts,
            r.LastErrorMessage,
            r.LastStatusCode,
            r.Status,
            r.FailedAt,
            r.ReplayedAt);
}
