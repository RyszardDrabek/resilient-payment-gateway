namespace PaymentGateway.Application.Locking;

public interface IDistributedLock : IAsyncDisposable
{
    string ResourceName { get; }
}
