using PaymentGateway.Domain.Enums;

namespace PaymentGateway.Domain.Entities;

public sealed class RiskVerdict
{
    public string Id { get; private set; } = string.Empty;
    public string EventId { get; private set; } = string.Empty;
    public string PaymentId { get; private set; } = string.Empty;
    public string PartyId { get; private set; } = string.Empty;
    public RiskVerdictStatus Status { get; private set; }
    public string? Reason { get; private set; }
    public string? FlagId { get; private set; }
    public DateTimeOffset ScoredAt { get; private set; }

    private RiskVerdict() { } // EF Core

    public RiskVerdict(
        string id,
        string eventId,
        string paymentId,
        string partyId,
        RiskVerdictStatus status,
        string? reason,
        string? flagId,
        DateTimeOffset scoredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(paymentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(partyId);

        Id = id;
        EventId = eventId;
        PaymentId = paymentId;
        PartyId = partyId;
        Status = status;
        Reason = reason;
        FlagId = flagId;
        ScoredAt = scoredAt;
    }

    public static RiskVerdict Clean(string id, string eventId, string paymentId, string partyId, string? reason, DateTimeOffset scoredAt) =>
        new(id, eventId, paymentId, partyId, RiskVerdictStatus.Clean, reason ?? "Risk score within normal threshold", null, scoredAt);

    public static RiskVerdict Anomalous(string id, string eventId, string paymentId, string partyId, string reason, string flagId, DateTimeOffset scoredAt) =>
        new(id, eventId, paymentId, partyId, RiskVerdictStatus.Anomalous, reason, flagId, scoredAt);

    public static RiskVerdict Degraded(string id, string eventId, string paymentId, string partyId, string reason, DateTimeOffset scoredAt) =>
        new(id, eventId, paymentId, partyId, RiskVerdictStatus.Degraded, reason, null, scoredAt);
}
