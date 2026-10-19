using MediatR;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Application.Reconciliation;

public record GetReconciliationSummaryQuery(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null) : IRequest<ReconciliationSummaryDto>;

public sealed class GetReconciliationSummaryQueryHandler(IReconciliationRepository reconciliationRepository)
    : IRequestHandler<GetReconciliationSummaryQuery, ReconciliationSummaryDto>
{
    public async Task<ReconciliationSummaryDto> Handle(GetReconciliationSummaryQuery request, CancellationToken ct)
    {
        var to = request.To ?? DateTimeOffset.UtcNow;
        var from = request.From ?? to.AddDays(-30);

        var summary = await reconciliationRepository.GetSummaryAsync(from, to, ct);
        var matchRate = summary.TotalRecords > 0
            ? Math.Round((double)summary.MatchedCount / summary.TotalRecords * 100.0, 2)
            : 100.0;

        return new ReconciliationSummaryDto(
            summary.TotalRecords,
            summary.MatchedCount,
            summary.DiscrepancyCount,
            summary.MatchedVolume,
            summary.DiscrepancyVolume,
            matchRate);
    }
}
