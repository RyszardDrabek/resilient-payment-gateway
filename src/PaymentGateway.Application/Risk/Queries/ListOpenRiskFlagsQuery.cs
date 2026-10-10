using MediatR;
using PaymentGateway.Application.Risk.Models;
using PaymentGateway.Application.Risk.Ports;

namespace PaymentGateway.Application.Risk.Queries;

public sealed record ListOpenRiskFlagsQuery(int Page = 1, int PageSize = 50) : IRequest<OpenRiskFlagsResponse>;

public sealed class ListOpenRiskFlagsQueryHandler(IRiskFlagRepository repository)
    : IRequestHandler<ListOpenRiskFlagsQuery, OpenRiskFlagsResponse>
{
    public async Task<OpenRiskFlagsResponse> Handle(ListOpenRiskFlagsQuery request, CancellationToken ct)
    {
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize switch
        {
            < 1 => 50,
            > 100 => 100,
            _ => request.PageSize
        };

        var (items, totalCount) = await repository.GetOpenFlagsAsync(page, pageSize, ct);
        var dtos = items.Select(RiskFlagDto.FromDomain).ToList();

        return new OpenRiskFlagsResponse(dtos, page, pageSize, totalCount);
    }
}
