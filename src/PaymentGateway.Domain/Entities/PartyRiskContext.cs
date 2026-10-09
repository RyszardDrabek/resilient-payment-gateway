namespace PaymentGateway.Domain.Entities;

public sealed class PartyRiskContext
{
    public string Id { get; private set; } = string.Empty;
    public string PartyId { get; private set; } = string.Empty;
    public string PaymentId { get; private set; } = string.Empty;
    public string EventId { get; private set; } = string.Empty;
    public long Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public float[] Embedding { get; private set; } = [];
    public DateTimeOffset RecordedAt { get; private set; }

    private PartyRiskContext() { } // EF Core

    public PartyRiskContext(
        string id,
        string partyId,
        string paymentId,
        string eventId,
        long amount,
        string currency,
        float[] embedding,
        DateTimeOffset recordedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(partyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(paymentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        Id = id;
        PartyId = partyId;
        PaymentId = paymentId;
        EventId = eventId;
        Amount = amount;
        Currency = currency;
        Embedding = embedding ?? [];
        RecordedAt = recordedAt;
    }
}
