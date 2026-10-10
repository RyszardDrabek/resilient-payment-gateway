namespace PaymentGateway.Domain.Entities;

public sealed class RiskFlag
{
    public string Id { get; private set; } = string.Empty;
    public string PaymentId { get; private set; } = string.Empty;
    public string PartyId { get; private set; } = string.Empty;
    public string EventId { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public string Status { get; private set; } = "Open";
    public string? Disposition { get; private set; }
    public DateTimeOffset? DispositionedAt { get; private set; }
    public string? ReviewerNotes { get; private set; }
    public DateTimeOffset RaisedAt { get; private set; }

    public static readonly IReadOnlySet<string> SupportedDispositions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "confirmed",
        "false_positive",
        "escalated"
    };

    public bool IsDispositioned => Disposition is not null;

    private RiskFlag() { } // EF Core

    public RiskFlag(string id, string paymentId, string partyId, string eventId, string reason, DateTimeOffset raisedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(paymentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(partyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        Id = id;
        PaymentId = paymentId;
        PartyId = partyId;
        EventId = eventId;
        Reason = reason;
        Status = "Open";
        RaisedAt = raisedAt;
    }

    public void ApplyDisposition(string disposition, DateTimeOffset dispositionedAt, string? reviewerNotes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(disposition);
        var normalized = disposition.Trim().ToLowerInvariant();
        if (!SupportedDispositions.Contains(normalized))
        {
            throw new ArgumentException($"Unsupported disposition '{disposition}'. Supported values: confirmed, false_positive, escalated.", nameof(disposition));
        }

        if (IsDispositioned)
        {
            throw new InvalidOperationException($"Risk flag '{Id}' is already dispositioned.");
        }

        Disposition = normalized;
        Status = "Dispositioned";
        DispositionedAt = dispositionedAt;
        ReviewerNotes = string.IsNullOrWhiteSpace(reviewerNotes) ? null : reviewerNotes.Trim();
    }
}
