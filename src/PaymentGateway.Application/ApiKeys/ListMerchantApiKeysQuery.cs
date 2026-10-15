using MediatR;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Application.ApiKeys;

public sealed record ListMerchantApiKeysQuery(string MerchantId)
    : IRequest<IReadOnlyList<MerchantApiKeySummaryDto>>;

public sealed class ListMerchantApiKeysQueryHandler(IMerchantApiKeyRepository repository)
    : IRequestHandler<ListMerchantApiKeysQuery, IReadOnlyList<MerchantApiKeySummaryDto>>
{
    public async Task<IReadOnlyList<MerchantApiKeySummaryDto>> Handle(
        ListMerchantApiKeysQuery request,
        CancellationToken ct)
    {
        var keys = await repository.ListByMerchantAsync(request.MerchantId, ct);

        return keys.Select(k => new MerchantApiKeySummaryDto(
            Id: k.Id,
            MerchantId: k.MerchantId,
            Name: k.Name,
            Prefix: k.Prefix,
            Scopes: k.Scopes,
            CreatedAt: k.CreatedAt,
            IsRevoked: k.IsRevoked,
            RevokedAt: k.RevokedAt,
            RevokedBy: k.RevokedBy)).ToList();
    }
}
