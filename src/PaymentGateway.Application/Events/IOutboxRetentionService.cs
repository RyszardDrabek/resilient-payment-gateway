namespace PaymentGateway.Application.Events;

public interface IOutboxRetentionService
{
    Task<int> CleanupExpiredOutboxMessagesAsync(CancellationToken ct = default);
}
