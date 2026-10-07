using System.Diagnostics;
using System.Text.RegularExpressions;
using OpenTelemetry;

namespace PaymentGateway.Edge.Telemetry;

/// <summary>
/// OpenTelemetry <see cref="BaseProcessor{T}"/> that scrubs payment-instrument values
/// (PAN, 32-char hex tokens, IBAN-class account numbers) from span attributes before export.
/// Satisfies F-EDGE-03 AC-1 / AC-2 / AC-3: masking happens inside the process boundary,
/// before any exporter receives the activity.
/// </summary>
public sealed class InstrumentMaskingProcessor : BaseProcessor<Activity>
{
    // ── Patterns ─────────────────────────────────────────────────────────────

    /// <summary>13–19 digit card PAN with optional spaces or hyphens between groups.</summary>
    private static readonly Regex PanPattern = new(
        @"\b\d{4}[- ]?\d{4}[- ]?\d{4}[- ]?\d{1,4}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        matchTimeout: TimeSpan.FromMilliseconds(100));

    /// <summary>32 lower/upper-hex chars (e.g. a Braintree/Stripe-style payment token).</summary>
    private static readonly Regex TokenPattern = new(
        @"\b[0-9a-fA-F]{32}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        matchTimeout: TimeSpan.FromMilliseconds(100));

    /// <summary>IBAN: 2-letter country code + 2 check digits + 11-30 BBAN chars.</summary>
    private static readonly Regex IbanPattern = new(
        @"\b[A-Z]{2}\d{2}[A-Z0-9]{11,30}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        matchTimeout: TimeSpan.FromMilliseconds(100));

    // ── Masking evaluator (applied in order: PAN → Token → IBAN) ─────────────

    private static readonly MatchEvaluator Masker = m =>
    {
        // Irreversible mask: keep last 4 chars for debuggability.
        var raw = m.Value;
        var suffix = raw.Length >= 4 ? raw[^4..] : raw;
        return $"****{suffix}";
    };

    // ── BaseProcessor<Activity> ───────────────────────────────────────────────

    /// <summary>
    /// Runs synchronously after the activity ends, before export. Replaces sensitive
    /// string-valued tags in-place. Non-string tags are never touched.
    /// </summary>
    public override void OnEnd(Activity activity)
    {
        if (activity is null)
        {
            return;
        }

        // Collect keys of string tags that need rewriting (avoid mutating during iteration).
        List<(string Key, string Masked)>? rewrites = null;

        foreach (var tag in activity.Tags)
        {
            if (tag.Value is not string raw || raw.Length == 0)
            {
                continue;
            }

            try
            {
                var masked = ApplyMasking(raw);
                if (!string.Equals(masked, raw, StringComparison.Ordinal))
                {
                    rewrites ??= [];
                    rewrites.Add((tag.Key, masked));
                }
            }
            catch
            {
                // Fail-safe: never crash the telemetry pipeline. Leave attribute as-is.
            }
        }

        if (rewrites is not null)
        {
            foreach (var (key, masked) in rewrites)
            {
                activity.SetTag(key, masked);
            }
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string ApplyMasking(string value)
    {
        // Apply patterns in priority order. Each pass may replace multiple occurrences.
        var result = PanPattern.Replace(value, Masker);
        result = TokenPattern.Replace(result, Masker);
        result = IbanPattern.Replace(result, Masker);
        return result;
    }
}
