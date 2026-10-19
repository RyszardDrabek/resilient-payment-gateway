using MediatR;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Application.Reconciliation;

public record ListDiscrepanciesQuery(int Skip = 0, int Take = 50) : IRequest<IReadOnlyList<ReconciliationRecordDto>>;

public sealed class ListDiscrepanciesQueryHandler(IReconciliationRepository reconciliationRepository)
    : IRequestHandler<ListDiscrepanciesQuery, IReadOnlyList<ReconciliationRecordDto>>
{
    public async Task<IReadOnlyList<ReconciliationRecordDto>> Handle(ListDiscrepanciesQuery request, CancellationToken ct)
    {
        var records = await reconciliationRepository.ListDiscrepanciesAsync(request.Skip, request.Take, ct);
        return records.Select(r => r.ToDto()).ToList();
    }
}
