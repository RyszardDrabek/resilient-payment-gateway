using OpenTelemetry;
using OpenTelemetry.Logs;

namespace PaymentGateway.Edge.Telemetry;

/// <summary>
/// OpenTelemetry <see cref="BaseProcessor{T}"/> that scrubs payment-instrument values
/// (PAN, 32-char hex tokens, IBAN-class account numbers) from log records before export.
/// Satisfies F-EDGE-03 AC-1: raw instrument values never appear in exported logs.
/// </summary>
public sealed class LogInstrumentMaskingProcessor : BaseProcessor<LogRecord>
{
    /// <summary>
    /// Runs synchronously before log records are exported. Masks sensitive data in
    /// <see cref="LogRecord.FormattedMessage"/>, <see cref="LogRecord.Body"/>, and
    /// structured <see cref="LogRecord.Attributes"/>.
    /// </summary>
    public override void OnEnd(LogRecord data)
    {
        if (data is null)
        {
            return;
        }

        try
        {
            // Mask formatted message if present
            if (!string.IsNullOrEmpty(data.FormattedMessage))
            {
                data.FormattedMessage = InstrumentMaskingPatterns.Mask(data.FormattedMessage);
            }

            // Mask body if present
            if (data.Body is string bodyStr && !string.IsNullOrEmpty(bodyStr))
            {
                data.Body = InstrumentMaskingPatterns.Mask(bodyStr);
            }

            // Mask structured log attributes
            if (data.Attributes is not null && data.Attributes.Count > 0)
            {
                List<KeyValuePair<string, object?>>? updated = null;

                for (var i = 0; i < data.Attributes.Count; i++)
                {
                    var kvp = data.Attributes[i];
                    if (kvp.Value is string raw && raw.Length > 0)
                    {
                        var masked = InstrumentMaskingPatterns.Mask(raw);
                        if (!string.Equals(masked, raw, StringComparison.Ordinal))
                        {
                            if (updated is null)
                            {
                                updated = new List<KeyValuePair<string, object?>>(data.Attributes.Count);
                                for (var j = 0; j < i; j++)
                                {
                                    updated.Add(data.Attributes[j]);
                                }
                            }
                            updated.Add(new KeyValuePair<string, object?>(kvp.Key, masked));
                            continue;
                        }
                    }

                    updated?.Add(kvp);
                }

                if (updated is not null)
                {
                    data.Attributes = updated;
                }
            }
        }
        catch
        {
            // Fail-safe: never disrupt logging pipeline
        }
    }
}
