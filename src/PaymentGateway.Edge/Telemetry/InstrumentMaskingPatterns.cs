using System.Text.RegularExpressions;

namespace PaymentGateway.Edge.Telemetry;

/// <summary>
/// Central regex patterns and masking logic for scrubbing payment-instrument values
/// (PAN, 32-char hex tokens, IBAN-class account numbers) across traces and logs (F-EDGE-03).
/// </summary>
public static class InstrumentMaskingPatterns
{
    // ── Patterns ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 13–19 digit card PAN with optional spaces or hyphens between digits.
    /// Supports standard 16-digit cards, 15-digit Amex (4-6-5), 14-digit Diners (4-6-4),
    /// and 17–19 digit cards (UnionPay, Maestro).
    /// </summary>
    private static readonly Regex PanPattern = new(
        @"\b(?:\d[ -]?){12,18}\d\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        matchTimeout: TimeSpan.FromMilliseconds(100));

    /// <summary>32 lower/upper-hex chars (e.g. payment/instrument token).</summary>
    private static readonly Regex TokenPattern = new(
        @"\b[0-9a-fA-F]{32}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        matchTimeout: TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// IBAN: 2-letter country code + 2 check digits + 11-30 BBAN chars.
    /// Supports unspaced electronic format and human-readable grouped format with spaces.
    /// </summary>
    private static readonly Regex IbanPattern = new(
        @"\b[A-Z]{2}\d{2}(?:[ ]?[A-Z0-9]){11,30}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        matchTimeout: TimeSpan.FromMilliseconds(100));

    // ── Masking evaluator (applied in order: PAN → Token → IBAN) ─────────────

    private static readonly MatchEvaluator Masker = m =>
    {
        var raw = m.Value;
        var alnum = string.Concat(raw.Where(char.IsLetterOrDigit));
        var suffix = alnum.Length >= 4 ? alnum[^4..] : alnum;
        return $"****{suffix}";
    };

    /// <summary>
    /// Applies regex patterns in priority order, returning the masked string.
    /// </summary>
    public static string Mask(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        // Apply IBAN first so spaced BBAN numeric blocks are not prematurely matched by PAN regex.
        var result = IbanPattern.Replace(value, Masker);
        result = PanPattern.Replace(result, Masker);
        result = TokenPattern.Replace(result, Masker);
        return result;
    }
}
