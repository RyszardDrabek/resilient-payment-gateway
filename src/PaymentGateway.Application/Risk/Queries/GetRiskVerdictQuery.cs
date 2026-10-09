using MediatR;
using PaymentGateway.Application.Risk.Ports;

namespace PaymentGateway.Application.Risk.Queries;

public sealed record RiskVerdictDto(
    string Id,
    string EventId,
    string PaymentId,
    string PartyId,
    string Status,
    string? Reason,
    string? FlagId,
    DateTimeOffset ScoredAt);

public sealed record GetRiskVerdictQuery(string Identifier) : IRequest<RiskVerdictDto?>;

public sealed class GetRiskVerdictQueryHandler(IRiskVerdictRepository verdictRepository)
    : IRequestHandler<GetRiskVerdictQuery, RiskVerdictDto?>
{
    public async Task<RiskVerdictDto?> Handle(GetRiskVerdictQuery request, CancellationToken cancellationToken)
    {
        var verdict = await verdictRepository.GetByIdentifierAsync(request.Identifier, cancellationToken);
        if (verdict is null)
        {
            return null;
        }

        return new RiskVerdictDto(
            verdict.Id,
            verdict.EventId,
            verdict.PaymentId,
            verdict.PartyId,
            verdict.Status.ToString().ToLowerInvariant(),
            verdict.Reason,
            verdict.FlagId,
            verdict.ScoredAt);
    }
}
