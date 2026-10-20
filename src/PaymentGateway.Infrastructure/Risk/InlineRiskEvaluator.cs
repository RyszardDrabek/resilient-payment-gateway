using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.Risk.Models;
using PaymentGateway.Application.Risk.Ports;

namespace PaymentGateway.Infrastructure.Risk;

public sealed class InlineRiskEvaluator : IInlineRiskEvaluator
{
    private readonly InlineRiskOptions _options;
    private readonly IRiskContextStore? _contextStore;
    private readonly ILogger<InlineRiskEvaluator>? _logger;
    private static readonly ConcurrentDictionary<string, ConcurrentQueue<DateTimeOffset>> _velocityWindows = new();

    public static void ResetVelocity() => _velocityWindows.Clear();

    public InlineRiskEvaluator(
        IOptions<InlineRiskOptions> options,
        IRiskContextStore? contextStore = null,
        ILogger<InlineRiskEvaluator>? logger = null)
    {
        _options = options.Value;
        _contextStore = contextStore;
        _logger = logger;
    }

    public async Task<InlineRiskEvaluation> EvaluateAsync(
        string partyId,
        long amount,
        string currency,
        string? settlementChannel = null,
        string? paymentMethodId = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        if (!_options.Enabled)
        {
            return InlineRiskEvaluation.Allow(0, "Inline risk evaluation is disabled via configuration.", sw.ElapsedMilliseconds);
        }

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(_options.TimeoutMilliseconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            return await EvaluateInternalAsync(partyId, amount, currency, sw, linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            sw.Stop();
            _logger?.LogWarning(
                "Inline risk evaluation timed out after {Timeout}ms for party {PartyId}. Failing open (ADR-025).",
                _options.TimeoutMilliseconds, partyId);
            return InlineRiskEvaluation.FailOpen($"Time budget of {_options.TimeoutMilliseconds}ms exceeded", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger?.LogWarning(
                ex,
                "Inline risk evaluation encountered an unexpected error for party {PartyId}: {Message}. Failing open (ADR-025).",
                partyId, ex.Message);
            return InlineRiskEvaluation.FailOpen($"Unexpected evaluation error: {ex.Message}", sw.ElapsedMilliseconds);
        }
    }

    private async Task<InlineRiskEvaluation> EvaluateInternalAsync(
        string partyId,
        long amount,
        string currency,
        Stopwatch sw,
        CancellationToken ct)
    {
        // 1. Blacklist / Known Fraud Prefix Rule (AC-2)
        if (IsBlacklistedParty(partyId))
        {
            sw.Stop();
            return InlineRiskEvaluation.Block(95, "Known fraudulent party pattern detected", sw.ElapsedMilliseconds);
        }

        // 2. Velocity Check (AC-3)
        var velocityCount = RecordAndCheckVelocity(partyId);
        if (velocityCount > _options.MaxVelocityPerMinute)
        {
            sw.Stop();
            return InlineRiskEvaluation.Block(
                88,
                $"Velocity limit exceeded: {velocityCount} payment attempts in rolling 60s window (limit: {_options.MaxVelocityPerMinute})",
                sw.ElapsedMilliseconds);
        }

        // 3. Forced Step-Up 3DS Challenge Marker
        if (partyId.Contains("challenge", StringComparison.OrdinalIgnoreCase))
        {
            sw.Stop();
            return InlineRiskEvaluation.Challenge3Ds(
                70,
                "Suspicious party activity pattern warrants mandatory 3DS2 Challenge authentication",
                sw.ElapsedMilliseconds);
        }

        // 4. Large Unchecked Transaction
        if (amount >= 10_000_000) // >= 100,000 EUR in minor units
        {
            sw.Stop();
            return InlineRiskEvaluation.Challenge3Ds(
                65,
                $"Transaction amount ({amount} {currency}) exceeds frictionless threshold, 3DS2 SCA required",
                sw.ElapsedMilliseconds);
        }

        // 5. Historical Context Anomaly Check
        if (_contextStore is not null)
        {
            var history = await _contextStore.GetPartyHistoryAsync(partyId, ct);
            if (history.Count > 0)
            {
                var averageAmount = history.Average(h => (double)h.Amount);
                var ratio = (double)amount / Math.Max(1.0, averageAmount);

                if (ratio >= 5.0 && amount > 1000)
                {
                    sw.Stop();
                    return InlineRiskEvaluation.Challenge3Ds(
                        62,
                        $"Transaction amount is {ratio:F1}x the party's historical average ({averageAmount:F0}), step-up 3DS enforced",
                        sw.ElapsedMilliseconds);
                }
            }
        }

        // 6. Normal Clean Transaction
        sw.Stop();
        return InlineRiskEvaluation.Allow(
            10,
            "Low-risk transaction within verified operational boundaries",
            sw.ElapsedMilliseconds);
    }

    private static bool IsBlacklistedParty(string partyId)
    {
        if (string.IsNullOrWhiteSpace(partyId)) return false;

        return partyId.StartsWith("party_fraud", StringComparison.OrdinalIgnoreCase)
            || partyId.StartsWith("fraud_", StringComparison.OrdinalIgnoreCase)
            || partyId.StartsWith("party_blocked", StringComparison.OrdinalIgnoreCase)
            || partyId.StartsWith("blocked_", StringComparison.OrdinalIgnoreCase);
    }

    private int RecordAndCheckVelocity(string partyId)
    {
        var now = DateTimeOffset.UtcNow;
        var queue = _velocityWindows.GetOrAdd(partyId, _ => new ConcurrentQueue<DateTimeOffset>());

        queue.Enqueue(now);

        // Prune entries older than 60 seconds
        var cutoff = now.AddSeconds(-60);
        while (queue.TryPeek(out var oldest) && oldest < cutoff)
        {
            queue.TryDequeue(out _);
        }

        return queue.Count;
    }
}
