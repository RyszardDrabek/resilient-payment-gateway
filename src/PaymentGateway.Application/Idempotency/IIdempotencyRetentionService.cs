namespace PaymentGateway.Application.Idempotency;

/// <summary>
/// Service coordinating cleanup of expired idempotency records and reaping of abandoned in-flight leases.
/// </summary>
public interface IIdempotencyRetentionService
{
    /// <summary>
    /// Deletes all idempotency records whose TTL has passed (<c>ExpiresAt &lt; now</c>).
    /// </summary>
    Task<int> CleanupExpiredRecordsAsync(CancellationToken ct = default);

    /// <summary>
    /// Identifies abandoned in-flight requests (<c>Status == InFlight</c> and <c>LockedUntil &lt; now</c>)
    /// and marks them as failed with a 504 status to unblock subsequent requests.
    /// </summary>
    Task<int> ReapAbandonedLeasesAsync(CancellationToken ct = default);
}
