using System.Diagnostics;
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
                var masked = InstrumentMaskingPatterns.Mask(raw);
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
}
