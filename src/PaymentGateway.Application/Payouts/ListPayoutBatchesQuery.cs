using MediatR;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Application.Payouts;

public record ListPayoutBatchesQuery(
    string MerchantId,
    int Skip = 0,
    int Take = 50) : IRequest<IReadOnlyList<MerchantPayoutBatchDto>>;

public sealed class ListPayoutBatchesQueryHandler(IPayoutRepository payoutRepository)
    : IRequestHandler<ListPayoutBatchesQuery, IReadOnlyList<MerchantPayoutBatchDto>>
{
    public async Task<IReadOnlyList<MerchantPayoutBatchDto>> Handle(ListPayoutBatchesQuery request, CancellationToken ct)
    {
        var batches = await payoutRepository.ListByMerchantIdAsync(request.MerchantId, request.Skip, request.Take, ct);
        return batches.Select(b => b.ToDto()).ToList();
    }
}
