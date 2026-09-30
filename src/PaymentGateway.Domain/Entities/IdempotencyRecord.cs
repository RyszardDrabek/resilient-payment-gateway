using PaymentGateway.Domain.Enums;

namespace PaymentGateway.Domain.Entities;

public sealed class IdempotencyRecord
{
    private IdempotencyRecord()
    {
        Key = string.Empty;
        CommandType = string.Empty;
        PayloadHash = string.Empty;
    }

    public Guid Id { get; private init; }
    public string Key { get; private init; }
    public string CommandType { get; private init; }
    public string? PaymentId { get; private set; }
    public string PayloadHash { get; private init; }
    public IdempotencyStatus Status { get; private set; }
    public int? ResponseStatusCode { get; private set; }
    public string? ResponsePayload { get; private set; }
    public DateTimeOffset CreatedAt { get; private init; }
    public DateTimeOffset ExpiresAt { get; private init; }
    public DateTimeOffset? LockedUntil { get; private set; }

    public static IdempotencyRecord CreateInFlight(
        string key,
        string commandType,
        string payloadHash,
        string? paymentId = null,
        TimeSpan? leaseDuration = null,
        TimeSpan? ttl = null,
        DateTimeOffset? now = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandType);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadHash);

        var currentTime = now ?? DateTimeOffset.UtcNow;
        var effectiveLease = leaseDuration ?? TimeSpan.FromMinutes(2);
        var effectiveTtl = ttl ?? TimeSpan.FromHours(24);

        return new IdempotencyRecord
        {
            Id = Guid.NewGuid(),
            Key = key,
            CommandType = commandType,
            PaymentId = paymentId,
            PayloadHash = payloadHash,
            Status = IdempotencyStatus.InFlight,
            CreatedAt = currentTime,
            ExpiresAt = currentTime.Add(effectiveTtl),
            LockedUntil = currentTime.Add(effectiveLease)
        };
    }

    public void Complete(int statusCode, string responsePayload)
    {
        Status = IdempotencyStatus.Completed;
        ResponseStatusCode = statusCode;
        ResponsePayload = responsePayload;
        LockedUntil = null;
    }

    public void Fail(int statusCode, string responsePayload)
    {
        Status = IdempotencyStatus.Failed;
        ResponseStatusCode = statusCode;
        ResponsePayload = responsePayload;
        LockedUntil = null;
    }

    public bool IsLeaseActive(DateTimeOffset utcNow) =>
        Status == IdempotencyStatus.InFlight && LockedUntil.HasValue && LockedUntil.Value > utcNow;

    public bool IsExpired(DateTimeOffset utcNow) =>
        ExpiresAt <= utcNow;

    public bool MatchesPayload(string payloadHash) =>
        string.Equals(PayloadHash, payloadHash, StringComparison.Ordinal);
}
