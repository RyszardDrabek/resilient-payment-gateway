namespace PaymentGateway.Application.Locking;

public interface IDistributedLockProvider
{
    Task<IDistributedLock?> TryAcquireLockAsync(string resourceName, CancellationToken ct = default);
}
