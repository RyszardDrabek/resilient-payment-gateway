namespace PaymentGateway.Domain.Ports;

/// <summary>
/// Unit of work port coordinating atomic transactions and persistence across domain aggregates.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Persists all pending tracked changes across aggregates within the current unit of work.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Executes the specified asynchronous action inside an atomic database transaction.
    /// If an exception occurs, the transaction is automatically rolled back.
    /// </summary>
    Task ExecuteTransactionAsync(Func<CancellationToken, Task> action, CancellationToken ct = default);

    /// <summary>
    /// Executes the specified asynchronous function inside an atomic database transaction and returns the result.
    /// If an exception occurs, the transaction is automatically rolled back.
    /// </summary>
    Task<T> ExecuteTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct = default);
}
