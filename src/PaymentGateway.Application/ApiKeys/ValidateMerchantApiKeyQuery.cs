using MediatR;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Application.ApiKeys;

public sealed record ValidateMerchantApiKeyQuery(string RawKey)
    : IRequest<ValidatedApiKeyDto>;

public sealed class ValidateMerchantApiKeyQueryHandler(IMerchantApiKeyRepository repository)
    : IRequestHandler<ValidateMerchantApiKeyQuery, ValidatedApiKeyDto>
{
    public async Task<ValidatedApiKeyDto> Handle(ValidateMerchantApiKeyQuery request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RawKey))
        {
            return new ValidatedApiKeyDto(false, null, null);
        }

        // Format is {prefix}_{secretHex} where prefix is e.g. "rpg_live_ab12cd" (15 chars)
        var parts = request.RawKey.Split('_');
        if (parts.Length < 3)
        {
            return new ValidatedApiKeyDto(false, null, null);
        }

        var prefix = $"{parts[0]}_{parts[1]}_{parts[2]}";
        var key = await repository.FindByPrefixAsync(prefix, ct);
        if (key is null || key.IsRevoked)
        {
            return new ValidatedApiKeyDto(false, null, null);
        }

        if (!key.VerifyKey(request.RawKey))
        {
            return new ValidatedApiKeyDto(false, null, null);
        }

        return new ValidatedApiKeyDto(true, key.MerchantId, key.Scopes);
    }
}
