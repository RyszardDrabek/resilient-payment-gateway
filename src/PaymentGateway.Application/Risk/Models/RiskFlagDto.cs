using PaymentGateway.Domain.Entities;

namespace PaymentGateway.Application.Risk.Models;

public sealed record RiskFlagDto(
    string Id,
    string PaymentId,
    string PartyId,
    string EventId,
    string Reason,
    string Status,
    string? Disposition,
    DateTimeOffset RaisedAt,
    DateTimeOffset? DispositionedAt)
{
    public static RiskFlagDto FromDomain(RiskFlag flag) =>
        new(
            flag.Id,
            flag.PaymentId,
            flag.PartyId,
            flag.EventId,
            flag.Reason,
            flag.Status,
            flag.Disposition,
            flag.RaisedAt,
            flag.DispositionedAt);
}

public sealed record OpenRiskFlagsResponse(
    IReadOnlyList<RiskFlagDto> Items,
    int Page,
    int PageSize,
    int TotalCount);
