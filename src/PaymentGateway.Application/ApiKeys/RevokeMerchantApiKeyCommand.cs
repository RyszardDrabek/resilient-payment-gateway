using MediatR;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Application.ApiKeys;

public sealed record RevokeMerchantApiKeyCommand(string MerchantId, Guid KeyId, string? RevokedBy = null)
    : IRequest<bool>;

public sealed class RevokeMerchantApiKeyCommandHandler(IMerchantApiKeyRepository repository)
    : IRequestHandler<RevokeMerchantApiKeyCommand, bool>
{
    public async Task<bool> Handle(RevokeMerchantApiKeyCommand request, CancellationToken ct)
    {
        var key = await repository.GetByIdAsync(request.KeyId, ct);
        if (key is null || !string.Equals(key.MerchantId, request.MerchantId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        key.Revoke(request.RevokedBy);
        await repository.UpdateAsync(key, ct);
        return true;
    }
}
