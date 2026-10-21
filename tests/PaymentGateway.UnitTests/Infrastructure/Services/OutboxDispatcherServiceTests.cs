using System.Text.Json;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.Events;
using PaymentGateway.Infrastructure.Options;
using PaymentGateway.Infrastructure.Persistence;
using PaymentGateway.Infrastructure.Services;
using Xunit;

namespace PaymentGateway.UnitTests.Infrastructure.Services;

public sealed class OutboxDispatcherServiceTests
{
    private sealed class FakePublishEndpoint : IPublishEndpoint
    {
        public List<object> PublishedMessages { get; } = [];
        public bool ShouldThrow { get; set; }

        public ConnectHandle ConnectPublishObserver(IPublishObserver observer) => throw new NotImplementedException();

        public Task Publish<T>(T message, CancellationToken cancellationToken = default) where T : class
        {
            if (ShouldThrow)
            {
                throw new InvalidOperationException("Publish failure simulation");
            }

            PublishedMessages.Add(message);
            return Task.CompletedTask;
        }

        public Task Publish<T>(T message, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class
        {
            if (ShouldThrow)
            {
                throw new InvalidOperationException("Publish failure simulation");
            }

            PublishedMessages.Add(message);
            return Task.CompletedTask;
        }

        public Task Publish<T>(T message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class
        {
            if (ShouldThrow)
            {
                throw new InvalidOperationException("Publish failure simulation");
            }

            PublishedMessages.Add(message);
            return Task.CompletedTask;
        }

        public Task Publish(object message, CancellationToken cancellationToken = default)
        {
            if (ShouldThrow)
            {
                throw new InvalidOperationException("Publish failure simulation");
            }

            PublishedMessages.Add(message);
            return Task.CompletedTask;
        }

        public Task Publish(object message, Type messageType, CancellationToken cancellationToken = default)
        {
            if (ShouldThrow)
            {
                throw new InvalidOperationException("Publish failure simulation");
            }

            PublishedMessages.Add(message);
            return Task.CompletedTask;
        }

        public Task Publish(object message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default)
        {
            if (ShouldThrow)
            {
                throw new InvalidOperationException("Publish failure simulation");
            }

            PublishedMessages.Add(message);
            return Task.CompletedTask;
        }

        public Task Publish(object message, Type messageType, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default)
        {
            if (ShouldThrow)
            {
                throw new InvalidOperationException("Publish failure simulation");
            }

            PublishedMessages.Add(message);
            return Task.CompletedTask;
        }

        public Task Publish<T>(object values, CancellationToken cancellationToken = default) where T : class => throw new NotImplementedException();
        public Task Publish<T>(object values, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class => throw new NotImplementedException();
        public Task Publish<T>(object values, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class => throw new NotImplementedException();
    }

    private static (IServiceScopeFactory ScopeFactory, PaymentDbContext DbContext, FakePublishEndpoint PublishEndpoint) CreateTestContext(string dbName)
    {
        var services = new ServiceCollection();
        var fakePublish = new FakePublishEndpoint();

        services.AddDbContext<PaymentDbContext>(options =>
            options.UseInMemoryDatabase(databaseName: dbName));
        services.AddSingleton<IPublishEndpoint>(fakePublish);

        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var db = provider.GetRequiredService<PaymentDbContext>();

        return (scopeFactory, db, fakePublish);
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_WhenNoMessages_ReturnsZero()
    {
        var (scopeFactory, _, publishEndpoint) = CreateTestContext(nameof(ProcessPendingMessagesAsync_WhenNoMessages_ReturnsZero));
        var options = Options.Create(new OutboxOptions { Enabled = true, BatchSize = 10, MaxDeliveryAttempts = 5 });
        var dispatcher = new OutboxDispatcherService(scopeFactory, options, NullLogger<OutboxDispatcherService>.Instance);

        var processed = await dispatcher.ProcessPendingMessagesAsync();

        processed.Should().Be(0);
        publishEndpoint.PublishedMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_DispatchesPendingLifecycleEvent_AndMarksProcessed()
    {
        var (scopeFactory, db, publishEndpoint) = CreateTestContext(nameof(ProcessPendingMessagesAsync_DispatchesPendingLifecycleEvent_AndMarksProcessed));
        var options = Options.Create(new OutboxOptions { Enabled = true, BatchSize = 10, MaxDeliveryAttempts = 5 });
        var dispatcher = new OutboxDispatcherService(scopeFactory, options, NullLogger<OutboxDispatcherService>.Instance);

        var evt = new PaymentLifecycleEvent(
            EventId: Guid.NewGuid().ToString("N"),
            PaymentId: "pay_123",
            PartyId: "merchant_abc",
            Outcome: "authorized",
            Amount: 1000,
            Currency: "EUR",
            Version: 1,
            OccurredAt: DateTimeOffset.UtcNow);

        db.OutboxMessages.Add(new OutboxMessageRecord
        {
            EventType = PaymentLifecycleEvent.EventType,
            Payload = JsonSerializer.Serialize(evt),
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            ProcessedAt = null,
            DeliveryAttempts = 0
        });
        await db.SaveChangesAsync();

        var processed = await dispatcher.ProcessPendingMessagesAsync();

        processed.Should().Be(1);
        publishEndpoint.PublishedMessages.Should().HaveCount(1);
        var publishedEvent = publishEndpoint.PublishedMessages[0] as PaymentLifecycleEvent;
        publishedEvent.Should().NotBeNull();
        publishedEvent!.PaymentId.Should().Be("pay_123");

        db.ChangeTracker.Clear();
        var updated = await db.OutboxMessages.FirstAsync();
        updated.ProcessedAt.Should().NotBeNull();
        updated.DeliveryAttempts.Should().Be(1);
        updated.Error.Should().BeNull();
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_WhenPublishFails_IncrementsDeliveryAttemptsAndRecordsError()
    {
        var (scopeFactory, db, publishEndpoint) = CreateTestContext(nameof(ProcessPendingMessagesAsync_WhenPublishFails_IncrementsDeliveryAttemptsAndRecordsError));
        publishEndpoint.ShouldThrow = true;

        var options = Options.Create(new OutboxOptions { Enabled = true, BatchSize = 10, MaxDeliveryAttempts = 3 });
        var dispatcher = new OutboxDispatcherService(scopeFactory, options, NullLogger<OutboxDispatcherService>.Instance);

        var evt = new PaymentLifecycleEvent(
            EventId: Guid.NewGuid().ToString("N"),
            PaymentId: "pay_fail",
            PartyId: "merchant_fail",
            Outcome: "authorized",
            Amount: 2000,
            Currency: "USD",
            Version: 1,
            OccurredAt: DateTimeOffset.UtcNow);

        db.OutboxMessages.Add(new OutboxMessageRecord
        {
            EventType = PaymentLifecycleEvent.EventType,
            Payload = JsonSerializer.Serialize(evt),
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            ProcessedAt = null,
            DeliveryAttempts = 1
        });
        await db.SaveChangesAsync();

        var processed = await dispatcher.ProcessPendingMessagesAsync();

        processed.Should().Be(1);
        db.ChangeTracker.Clear();
        var updated = await db.OutboxMessages.FirstAsync();
        updated.ProcessedAt.Should().BeNull();
        updated.DeliveryAttempts.Should().Be(2);
        updated.Error.Should().Contain("Publish failure simulation");
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_SkipsMessagesExceedingMaxDeliveryAttempts()
    {
        var (scopeFactory, db, publishEndpoint) = CreateTestContext(nameof(ProcessPendingMessagesAsync_SkipsMessagesExceedingMaxDeliveryAttempts));
        var options = Options.Create(new OutboxOptions { Enabled = true, BatchSize = 10, MaxDeliveryAttempts = 3 });
        var dispatcher = new OutboxDispatcherService(scopeFactory, options, NullLogger<OutboxDispatcherService>.Instance);

        db.OutboxMessages.Add(new OutboxMessageRecord
        {
            EventType = PaymentLifecycleEvent.EventType,
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            ProcessedAt = null,
            DeliveryAttempts = 3,
            Error = "Already dead-lettered"
        });
        await db.SaveChangesAsync();

        var processed = await dispatcher.ProcessPendingMessagesAsync();

        processed.Should().Be(0);
        publishEndpoint.PublishedMessages.Should().BeEmpty();
    }
}
