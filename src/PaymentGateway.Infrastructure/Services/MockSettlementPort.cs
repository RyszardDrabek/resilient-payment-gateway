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
            : (configuration["PaymentGateway:ActiveChannel"] ?? "MOCK");

        // Mock decline trigger: partyId starting with "decline_"
        if (!string.IsNullOrEmpty(partyId) && partyId.StartsWith("decline_", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new SettlementResult(
                false,
                resolvedChannel,
                $"ref_declined_{Guid.NewGuid():N}",
                "Insufficient funds"));
        }

        var channelName = resolvedChannel.ToLowerInvariant();
        var channelRef = $"ref_{channelName}_{Guid.NewGuid():N}";
        return Task.FromResult(new SettlementResult(true, resolvedChannel, channelRef));
    }

    public Task<SettlementResult> CaptureAsync(
        string paymentId,
        string channelReference,
        long amount,
        string currency,
        string? channel = null,
        CancellationToken ct = default)
    {
        var resolvedChannel = !string.IsNullOrWhiteSpace(channel)
            ? channel
            : (configuration["PaymentGateway:ActiveChannel"] ?? "MOCK");

        var transitionRef = $"ref_cap_{Guid.NewGuid():N}";
        return Task.FromResult(SettlementResult.Success(resolvedChannel, transitionRef, paymentId));
    }

    public Task<SettlementResult> RefundAsync(
        string paymentId,
        string channelReference,
        long amount,
        string currency,
        string? channel = null,
        CancellationToken ct = default)
    {
        var resolvedChannel = !string.IsNullOrWhiteSpace(channel)
            ? channel
            : (configuration["PaymentGateway:ActiveChannel"] ?? "MOCK");

        var transitionRef = $"ref_ref_{Guid.NewGuid():N}";
        return Task.FromResult(SettlementResult.Success(resolvedChannel, transitionRef, paymentId));
    }

    public Task<SettlementResult> CancelAsync(
        string paymentId,
        string channelReference,
        string? channel = null,
        CancellationToken ct = default)
    {
        var resolvedChannel = !string.IsNullOrWhiteSpace(channel)
            ? channel
            : (configuration["PaymentGateway:ActiveChannel"] ?? "MOCK");

        var transitionRef = $"ref_cnc_{Guid.NewGuid():N}";
        return Task.FromResult(SettlementResult.Success(resolvedChannel, transitionRef, paymentId));
    }
}
