using Microsoft.Extensions.Configuration;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Infrastructure.Services;

public sealed class MockSettlementPort(IConfiguration configuration) : ISettlementPort
{
    public Task<SettlementResult> AuthorizeAsync(
        string partyId,
        long amount,
        string currency,
        string? channel = null,
        CancellationToken ct = default)
    {
        var resolvedChannel = !string.IsNullOrWhiteSpace(channel)
            ? channel
            : configuration["PaymentGateway:ActiveChannel"] ?? "MOCK";

        // Mock decline trigger: partyId starting with "decline_"
        if (partyId.StartsWith("decline_", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new SettlementResult(
                false,
                resolvedChannel,
                $"ref_declined_{Guid.NewGuid():N}",
                "Insufficient funds"));
        }

        var channelRef = $"ref_{resolvedChannel.ToLowerInvariant()}_{Guid.NewGuid():N}";
        return Task.FromResult(new SettlementResult(true, resolvedChannel, channelRef));
    }
}
