namespace PaymentGateway.Application.Fx;

/// <summary>
/// Service coordinating periodic background expiration of stale currency quotes.
/// </summary>
public interface IFxQuoteExpirationService
{
    /// <summary>
    /// Identifies all active quotes whose TTL has expired (<c>Status == Active</c> and <c>ExpiresAt &lt;= now</c>)
    /// and transitions them to <c>Status = Expired</c>.
    /// </summary>
    Task<int> ExpireStaleQuotesAsync(CancellationToken ct = default);
}
