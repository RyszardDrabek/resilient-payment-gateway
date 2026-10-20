using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.Webhooks;
using PaymentGateway.Domain.Ports;
using PaymentGateway.Infrastructure.Options;

namespace PaymentGateway.Infrastructure.Services;

public sealed class MerchantWebhookRetentionService(
    IMerchantWebhookDeadLetterRepository repository,
    IOptions<MerchantWebhookOptions> options,
    ILogger<MerchantWebhookRetentionService> logger,
    TimeProvider? timeProvider = null) : IMerchantWebhookRetentionService
{
    private readonly MerchantWebhookOptions _options = options.Value;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<int> CleanupExpiredDeadLettersAsync(CancellationToken ct = default)
    {
        var retentionDays = _options.DeadLetterRetentionDays > 0 ? _options.DeadLetterRetentionDays : 90;
        var cutoff = _timeProvider.GetUtcNow().AddDays(-retentionDays);

        logger.LogInformation(
            "Starting merchant webhook dead-letter cleanup for records failed before {Cutoff} (retention: {RetentionDays} days)",
            cutoff,
            retentionDays);

        var deletedCount = await repository.CleanupExpiredAsync(cutoff, ct);

        if (deletedCount > 0)
        {
            logger.LogInformation("Merchant webhook dead-letter cleanup finished. Pruned {DeletedCount} records.", deletedCount);
        }

        return deletedCount;
    }
}
