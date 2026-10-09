namespace PaymentGateway.Domain.Entities;

public sealed class RiskFlag
{
    public string Id { get; private set; } = string.Empty;
    public string PaymentId { get; private set; } = string.Empty;
    public string PartyId { get; private set; } = string.Empty;
    public string EventId { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public string Status { get; private set; } = "Open";
    public DateTimeOffset RaisedAt { get; private set; }

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
}
