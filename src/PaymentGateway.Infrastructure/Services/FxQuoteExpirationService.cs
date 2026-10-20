using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.Fx;
using PaymentGateway.Domain.Ports;
using PaymentGateway.Infrastructure.Options;

namespace PaymentGateway.Infrastructure.Services;

public sealed class FxQuoteExpirationService(
    IFxQuoteRepository repository,
    IOptions<FxQuoteExpirationOptions> options,
    ILogger<FxQuoteExpirationService> logger,
    TimeProvider? timeProvider = null) : IFxQuoteExpirationService
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly FxQuoteExpirationOptions _options = options.Value;

    public async Task<int> ExpireStaleQuotesAsync(CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow();
        var batchSize = _options.BatchSize > 0 ? _options.BatchSize : 1000;

        var expiredCount = await repository.ExpireStaleQuotesAsync(now, batchSize, ct);

        if (expiredCount > 0)
        {
            logger.LogInformation("FX quote expiration cycle completed. Expired {Count} stale quotes.", expiredCount);
        }

        return expiredCount;
    }
}
