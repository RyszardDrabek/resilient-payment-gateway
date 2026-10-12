using Microsoft.EntityFrameworkCore;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Ports;
using PaymentGateway.Infrastructure.Persistence;

namespace PaymentGateway.Infrastructure.Repositories;

public sealed class PaymentRepository(PaymentDbContext dbContext) : IPaymentRepository
{
    public async Task AddAsync(Payment payment, CancellationToken ct = default)
    {
        dbContext.Payments.Add(payment);
        DispatchDomainEventsToOutbox(payment);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Payment payment, CancellationToken ct = default)
    {
        dbContext.Payments.Update(payment);
        DispatchDomainEventsToOutbox(payment);
        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new PaymentGateway.Domain.Exceptions.PaymentConcurrencyException(payment.Id);
        }
    }

    public async Task<Payment?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        return await dbContext.Payments.FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<Payment?> GetByChannelReferenceAsync(string channelReference, CancellationToken ct = default)
    {
        return await dbContext.Payments.FirstOrDefaultAsync(p => p.ChannelReference == channelReference, ct);
    }

    public async Task<IReadOnlyList<Payment>> GetUnresolvedAsync(CancellationToken ct = default)
    {
        return await dbContext.Payments
            .Where(p => p.State == PaymentState.Pending || p.State == PaymentState.Unknown)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<(IReadOnlyList<Payment> Items, int TotalCount)> GetUnresolvedPaginatedAsync(
        DateTimeOffset createdBefore,
        string? channel = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default)
    {
        var query = dbContext.Payments
            .Where(p => (p.State == PaymentState.Pending || p.State == PaymentState.Unknown) &&
                        p.CreatedAt <= createdBefore);

        if (!string.IsNullOrWhiteSpace(channel))
        {
            var normalizedChannel = channel.Trim();
            query = query.Where(p => EF.Functions.ILike(p.SettlementChannel, normalizedChannel));
        }

        var totalCount = await query.CountAsync(ct);

        var safePage = page < 1 ? 1 : page;
        var safePageSize = pageSize < 1 ? 50 : (pageSize > 100 ? 100 : pageSize);

        var items = await query
            .OrderBy(p => p.CreatedAt)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }


    private void DispatchDomainEventsToOutbox(Payment payment)
    {
        foreach (var domainEvent in payment.DomainEvents)
        {
            if (domainEvent is PaymentGateway.Domain.Events.PaymentTransitionDomainEvent transition)
            {
                var lifecycleEvent = new PaymentGateway.Application.Events.PaymentLifecycleEvent(
                    EventId: $"evt_{Guid.NewGuid():N}",
                    PaymentId: transition.PaymentId,
                    PartyId: transition.PartyId,
                    Outcome: transition.Outcome.ToString().ToLowerInvariant(),
                    Amount: transition.Amount,
                    Currency: transition.Currency,
                    Version: transition.Version,
                    OccurredAt: transition.OccurredAt);

                var outboxMessage = new OutboxMessageRecord
                {
                    Id = Guid.NewGuid(),
                    EventType = PaymentGateway.Application.Events.PaymentLifecycleEvent.EventType,
                    Payload = System.Text.Json.JsonSerializer.Serialize(lifecycleEvent),
                    CreatedAt = DateTimeOffset.UtcNow
                };

                dbContext.OutboxMessages.Add(outboxMessage);
            }
        }

        payment.ClearDomainEvents();
    }
}
