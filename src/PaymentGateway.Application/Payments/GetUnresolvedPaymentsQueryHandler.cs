using MediatR;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Application.Payments;

public sealed class GetUnresolvedPaymentsQueryHandler(
    IPaymentRepository repository,
    TimeProvider? timeProvider = null)
    : IRequestHandler<GetUnresolvedPaymentsQuery, UnresolvedPaymentsResponse>
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<UnresolvedPaymentsResponse> Handle(GetUnresolvedPaymentsQuery request, CancellationToken ct)
    {
        var ageMinutes = request.AgeMinutes.GetValueOrDefault(15);
        if (ageMinutes < 0)
        {
            ageMinutes = 0;
        }

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize switch
        {
            < 1 => 50,
            > 100 => 100,
            _ => request.PageSize
        };

        var now = _timeProvider.GetUtcNow();
        var cutoff = now.AddMinutes(-ageMinutes);

        var (items, totalCount) = await repository.GetUnresolvedPaginatedAsync(
            cutoff,
            request.Channel,
            page,
            pageSize,
            ct);

        var dtos = items.Select(p => new UnresolvedPaymentSummaryDto(
            PaymentId: p.Id,
            State: p.State.ToString(),
            SettlementChannel: p.SettlementChannel,
            ChannelReference: p.ChannelReference,
            CreatedAt: p.CreatedAt,
            DurationElapsedMinutes: Math.Max(0, (int)(now - p.CreatedAt).TotalMinutes)
        )).ToList();

        return new UnresolvedPaymentsResponse(
            Items: dtos,
            Page: page,
            PageSize: pageSize,
            TotalCount: totalCount,
            AgeMinutes: ageMinutes,
            Channel: request.Channel);
    }
}
