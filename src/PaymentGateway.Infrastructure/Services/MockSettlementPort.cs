using Microsoft.Extensions.Configuration;
using PaymentGateway.Domain.Entities;
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

        // Mock decline trigger: partyId starting with "decline_" or ending with "0003"
        if (!string.IsNullOrEmpty(partyId) && (partyId.StartsWith("decline_", StringComparison.OrdinalIgnoreCase) || partyId.EndsWith("0003")))
        {
            return Task.FromResult(new SettlementResult(
                false,
                resolvedChannel,
                $"ref_declined_{Guid.NewGuid():N}",
                "Insufficient funds",
                MerchantReference: idempotencyKey));
        }

        // Mock 3DS challenge trigger: partyId containing "challenge", "3ds", or ending with "0002"
        if (!string.IsNullOrEmpty(partyId) && (partyId.Contains("challenge", StringComparison.OrdinalIgnoreCase) || partyId.Contains("3ds", StringComparison.OrdinalIgnoreCase) || partyId.EndsWith("0002")))
        {
            var challengeRef = $"ref_mock_3ds_{Guid.NewGuid():N}";
            return Task.FromResult(SettlementResult.ActionRequired(
                resolvedChannel,
                challengeRef,
                new ThreeDsActionDetails(
                    ActionType: "threeDS2",
                    TargetUrl: "https://mock-bank.local/3ds/challenge",
                    Token: $"mock_3ds_{Guid.NewGuid():N}",
                    ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(15)),
                merchantReference: idempotencyKey));
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

    public Task<SettlementResult> SubmitThreeDsDetailsAsync(
        string paymentId,
        string channelReference,
        string? threeDsResult,
        IReadOnlyDictionary<string, string>? details = null,
        string? channel = null,
        CancellationToken ct = default)
    {
        var resolvedChannel = !string.IsNullOrWhiteSpace(channel)
            ? channel
            : (configuration["PaymentGateway:ActiveChannel"] ?? "MOCK");

        if (string.Equals(threeDsResult, "failed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(threeDsResult, "refused", StringComparison.OrdinalIgnoreCase) ||
            (details != null && details.Values.Any(v => v.Contains("fail", StringComparison.OrdinalIgnoreCase) || v.Contains("refuse", StringComparison.OrdinalIgnoreCase))))
        {
            return Task.FromResult(SettlementResult.Declined(
                resolvedChannel,
                $"ref_mock_3ds_declined_{Guid.NewGuid():N}",
                "ThreeDsRefused",
                paymentId));
        }

        return Task.FromResult(SettlementResult.Success(
            resolvedChannel,
            $"ref_mock_3ds_auth_{Guid.NewGuid():N}",
            paymentId));
    }
}
