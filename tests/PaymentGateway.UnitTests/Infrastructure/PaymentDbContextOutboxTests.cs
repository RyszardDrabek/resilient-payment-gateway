using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PaymentGateway.Application.Events;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Events;
using PaymentGateway.Infrastructure.Persistence;
using Xunit;

namespace PaymentGateway.UnitTests.Infrastructure;

public sealed class PaymentDbContextOutboxTests
{
    private sealed record TestGenericDomainEvent(string TestProperty, DateTimeOffset OccurredAt) : IDomainEvent;

    private static DbContextOptions<PaymentDbContext> CreateInMemoryOptions(string dbName)
    {
        return new DbContextOptionsBuilder<PaymentDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
    }

    [Fact]
    public async Task ConvertDomainEventsToOutboxMessages_WhenDoubleCalled_IsIdempotentAndProducesNoDuplicates()
    {
        var options = CreateInMemoryOptions(nameof(ConvertDomainEventsToOutboxMessages_WhenDoubleCalled_IsIdempotentAndProducesNoDuplicates));
        await using var db = new PaymentDbContext(options);

        var payment = Payment.Authorize("party_test_idem", 5000, "EUR", "MOCK", "ref_test_1");
        db.Payments.Add(payment);

        payment.DomainEvents.Should().HaveCount(1);

        // First conversion (as would happen in interceptor SavingChanges)
        db.ConvertDomainEventsToOutboxMessages();

        payment.DomainEvents.Should().BeEmpty();
        db.OutboxMessages.Local.Should().HaveCount(1);

        // Second conversion (as would happen in DbContext SaveChangesAsync override)
        db.ConvertDomainEventsToOutboxMessages();

        // Must still be exactly 1 record — no duplicates produced
        db.OutboxMessages.Local.Should().HaveCount(1);
    }

    [Fact]
    public async Task SaveChangesAsync_WithoutInterceptor_AutomaticallyExtractsDomainEventsToOutbox()
    {
        var options = CreateInMemoryOptions(nameof(SaveChangesAsync_WithoutInterceptor_AutomaticallyExtractsDomainEventsToOutbox));
        await using var db = new PaymentDbContext(options);

        var payment = Payment.Authorize("party_direct_save", 12345, "USD", "MOCK", "ref_direct_uow");
        db.Payments.Add(payment);

        await db.SaveChangesAsync();

        payment.DomainEvents.Should().BeEmpty();

        var outboxMessage = await db.OutboxMessages.FirstOrDefaultAsync(m => m.Payload.Contains(payment.Id));
        outboxMessage.Should().NotBeNull();
        outboxMessage!.EventType.Should().Be(PaymentLifecycleEvent.EventType);

        var lifecycleEvent = JsonSerializer.Deserialize<PaymentLifecycleEvent>(outboxMessage.Payload);
        lifecycleEvent.Should().NotBeNull();
        lifecycleEvent!.PaymentId.Should().Be(payment.Id);
        lifecycleEvent.PartyId.Should().Be("party_direct_save");
        lifecycleEvent.Amount.Should().Be(12345);
        lifecycleEvent.Currency.Should().Be("USD");
        lifecycleEvent.Outcome.Should().Be("authorized");
    }

    [Fact]
    public async Task ConvertDomainEventsToOutboxMessages_WithUnmappedDomainEvent_CreatesGenericOutboxRecord()
    {
        var options = CreateInMemoryOptions(nameof(ConvertDomainEventsToOutboxMessages_WithUnmappedDomainEvent_CreatesGenericOutboxRecord));
        await using var db = new PaymentDbContext(options);

        var payment = Payment.Authorize("party_test_unmapped", 1000, "EUR", "MOCK", "ref_unmapped");
        payment.ClearDomainEvents();

        // Inject unmapped domain event into Payment's domain events list
        var field = typeof(Payment).GetField("_domainEvents", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var list = (List<IDomainEvent>)field!.GetValue(payment)!;
        list.Add(new TestGenericDomainEvent("CustomData", DateTimeOffset.UtcNow));

        db.Payments.Add(payment);

        db.ConvertDomainEventsToOutboxMessages();

        payment.DomainEvents.Should().BeEmpty();
        db.OutboxMessages.Local.Should().HaveCount(1);

        var record = db.OutboxMessages.Local.First();
        record.EventType.Should().Be("domain.testgenericdomainevent");
        record.Payload.Should().Contain("CustomData");
    }
}
