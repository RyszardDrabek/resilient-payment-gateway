using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Infrastructure.Services;

public sealed class MockSettlementPort : ISettlementPort
{
    public Task<SettlementResult> AuthorizeAsync(
        string partyId,
        long amount,
        string currency,
        string channel,
        CancellationToken ct = default)
    {
        // Mock decline trigger: partyId starting with "decline_"
        if (partyId.StartsWith("decline_", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new SettlementResult(false, $"ref_declined_{Guid.NewGuid():N}", "Insufficient funds"));
        }

        var channelRef = $"ref_{channel.ToLowerInvariant()}_{Guid.NewGuid():N}";
        return Task.FromResult(new SettlementResult(true, channelRef));
    }
}
