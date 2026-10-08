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
        CancellationToken ct = default) =>
        AuthorizeAsync(partyId, amount, currency, channel, null, ct);

    public Task<SettlementResult> AuthorizeAsync(
        string partyId,
        long amount,
        string currency,
        string? channel,
        string? idempotencyKey,
        CancellationToken ct = default)
    {
        var resolvedChannel = !string.IsNullOrWhiteSpace(channel)
            ? channel
            : (configuration["PaymentGateway:ActiveChannel"] ?? "MOCK");

        if (!string.IsNullOrEmpty(partyId) && (partyId.Contains("timeout", StringComparison.OrdinalIgnoreCase) || partyId.Contains("unanswered", StringComparison.OrdinalIgnoreCase)))
        {
            return Task.FromResult(SettlementResult.Unanswered(
                resolvedChannel,
                "Mock settlement channel failed to answer",
                idempotencyKey));
        }

        // Mock decline trigger: partyId starting with "decline_"
        if (!string.IsNullOrEmpty(partyId) && partyId.StartsWith("decline_", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new SettlementResult(
                false,
                resolvedChannel,
                $"ref_declined_{Guid.NewGuid():N}",
                "Insufficient funds",
                MerchantReference: idempotencyKey));
        }

        var channelName = resolvedChannel.ToLowerInvariant();
        var channelRef = $"ref_{channelName}_{Guid.NewGuid():N}";
        return Task.FromResult(new SettlementResult(true, resolvedChannel, channelRef, MerchantReference: idempotencyKey));
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
