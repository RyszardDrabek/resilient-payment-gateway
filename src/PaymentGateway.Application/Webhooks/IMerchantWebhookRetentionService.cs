namespace PaymentGateway.Application.Webhooks;

/// <summary>
/// Service coordinating periodic retention cleanup for merchant webhook dead-letter records.
/// </summary>
public interface IMerchantWebhookRetentionService
{
    /// <summary>
    /// Prunes dead-letter records older than the configured retention threshold.
    /// </summary>
    Task<int> CleanupExpiredDeadLettersAsync(CancellationToken ct = default);
}
