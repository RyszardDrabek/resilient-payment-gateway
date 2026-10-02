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
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<Payment?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        return await dbContext.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
    }
}
