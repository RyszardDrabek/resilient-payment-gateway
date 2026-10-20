using Microsoft.EntityFrameworkCore.Diagnostics;

namespace PaymentGateway.Infrastructure.Persistence;

/// <summary>
/// EF Core interceptor that intercepts save operations and automatically converts all in-memory
/// domain events from tracked entities implementing <see cref="PaymentGateway.Domain.Events.IHasDomainEvents"/>
/// into <see cref="OutboxMessageRecord"/> rows within the exact same database transaction.
/// </summary>
public sealed class DomainEventsToOutboxInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is PaymentDbContext dbContext)
        {
            dbContext.ConvertDomainEventsToOutboxMessages();
        }

        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is PaymentDbContext dbContext)
        {
            dbContext.ConvertDomainEventsToOutboxMessages();
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
