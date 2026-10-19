using MediatR;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Application.Payouts;

public record GetPayoutBatchQuery(string BatchId) : IRequest<MerchantPayoutBatchDto?>;

public sealed class GetPayoutBatchQueryHandler(IPayoutRepository payoutRepository)
    : IRequestHandler<GetPayoutBatchQuery, MerchantPayoutBatchDto?>
{
    public async Task<MerchantPayoutBatchDto?> Handle(GetPayoutBatchQuery request, CancellationToken ct)
    {
        var batch = await payoutRepository.GetByIdAsync(request.BatchId, ct);
        return batch?.ToDto();
    }
}
