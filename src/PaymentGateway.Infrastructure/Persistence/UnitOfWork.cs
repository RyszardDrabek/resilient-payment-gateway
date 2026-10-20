using Microsoft.EntityFrameworkCore;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IUnitOfWork"/> coordinating atomic transactions across aggregates.
/// </summary>
public sealed class UnitOfWork(PaymentDbContext dbContext) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        return dbContext.SaveChangesAsync(ct);
    }

    public async Task ExecuteTransactionAsync(Func<CancellationToken, Task> action, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (dbContext.Database.CurrentTransaction is not null)
        {
            await action(ct);
            return;
        }

        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);
            await action(ct);
            await transaction.CommitAsync(ct);
        });
    }

    public async Task<T> ExecuteTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (dbContext.Database.CurrentTransaction is not null)
        {
            return await action(ct);
        }

        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);
            var result = await action(ct);
            await transaction.CommitAsync(ct);
            return result;
        });
    }
}
