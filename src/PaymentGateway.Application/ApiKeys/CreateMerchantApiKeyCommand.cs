using MediatR;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Application.ApiKeys;

public sealed record CreateMerchantApiKeyCommand(
    string MerchantId,
    string Name,
    IReadOnlyList<string> Scopes,
    bool IsLive = true) : IRequest<MerchantApiKeyCreatedDto>;

public sealed class CreateMerchantApiKeyCommandHandler(IMerchantApiKeyRepository repository)
    : IRequestHandler<CreateMerchantApiKeyCommand, MerchantApiKeyCreatedDto>
{
    public async Task<MerchantApiKeyCreatedDto> Handle(CreateMerchantApiKeyCommand request, CancellationToken ct)
    {
        var (entity, rawKey) = MerchantApiKey.Create(
            merchantId: request.MerchantId,
            name: request.Name,
            scopes: request.Scopes,
            isLive: request.IsLive);

        await repository.AddAsync(entity, ct);

        return new MerchantApiKeyCreatedDto(
            Id: entity.Id,
            MerchantId: entity.MerchantId,
            Name: entity.Name,
            Prefix: entity.Prefix,
            Scopes: entity.Scopes,
            CreatedAt: entity.CreatedAt,
            RawKey: rawKey);
    }
}
