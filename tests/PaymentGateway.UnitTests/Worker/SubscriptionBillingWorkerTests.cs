using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PaymentGateway.Application.Payments;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Ports;
using PaymentGateway.Worker;
using Xunit;

namespace PaymentGateway.UnitTests.Worker;

public sealed class SubscriptionBillingWorkerTests
{
    [Fact]
    public async Task ProcessBillingCycleAsync_WhenDueSubscriptionsExist_ChargesWithMitAndAdvancesBilling()
    {
        var dueTime = DateTimeOffset.UtcNow.AddMinutes(-10);
        var sub1 = new Subscription("sub_1", "party_1", "pm_1", 1500, "EUR", SubscriptionInterval.Monthly, startDate: dueTime);

        var fakeRepo = new FakeSubscriptionRepository([sub1]);
        var fakeMediator = new FakeMediator(new PaymentDto(
            "pay_1",
            "party_1",
            1500,
            "EUR",
            "ADYEN",
            "Authorized",
            "ref_1",
            null,
            DateTimeOffset.UtcNow,
            TransactionOrigin: "MIT",
            SubscriptionId: "sub_1"));

        var services = new ServiceCollection();
        services.AddScoped<ISubscriptionRepository>(_ => fakeRepo);
        services.AddScoped<IMediator>(_ => fakeMediator);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var worker = new SubscriptionBillingWorker(scopeFactory, NullLogger<SubscriptionBillingWorker>.Instance);

        var result = await worker.ProcessBillingCycleAsync(CancellationToken.None);

        result.Should().Be(1);
        sub1.FailureCount.Should().Be(0);
        sub1.LastBilledAt.Should().NotBeNull();
        sub1.NextBillingAt.Should().BeAfter(dueTime);
        fakeMediator.SentCommands.Should().ContainSingle();
        var cmd = fakeMediator.SentCommands[0];
        cmd.TransactionOrigin.Should().Be("MIT");
        cmd.SubscriptionId.Should().Be("sub_1");
        cmd.PaymentMethodId.Should().Be("pm_1");
    }

    [Fact]
    public async Task ProcessBillingCycleAsync_WhenChargeFails_RecordsBillingFailure()
    {
        var dueTime = DateTimeOffset.UtcNow.AddMinutes(-10);
        var sub1 = new Subscription("sub_1", "party_1", "pm_1", 1500, "EUR", SubscriptionInterval.Monthly, startDate: dueTime);

        var fakeRepo = new FakeSubscriptionRepository([sub1]);
        var fakeMediator = new FakeMediator(new PaymentDto(
            "pay_1",
            "party_1",
            1500,
            "EUR",
            "ADYEN",
            "Declined",
            "ref_1",
            "Refused",
            DateTimeOffset.UtcNow,
            TransactionOrigin: "MIT",
            SubscriptionId: "sub_1"));

        var services = new ServiceCollection();
        services.AddScoped<ISubscriptionRepository>(_ => fakeRepo);
        services.AddScoped<IMediator>(_ => fakeMediator);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var worker = new SubscriptionBillingWorker(scopeFactory, NullLogger<SubscriptionBillingWorker>.Instance);

        var result = await worker.ProcessBillingCycleAsync(CancellationToken.None);

        result.Should().Be(1);
        sub1.FailureCount.Should().Be(1);
        sub1.LastBilledAt.Should().BeNull();
    }

    [Fact]
    public async Task ProcessBillingCycleAsync_WhenNoDueSubscriptions_ReturnsZero()
    {
        var fakeRepo = new FakeSubscriptionRepository([]);
        var fakeMediator = new FakeMediator(new PaymentDto("pay_1", "p", 1, "EUR", "M", "Authorized", null, null, DateTimeOffset.UtcNow));

        var services = new ServiceCollection();
        services.AddScoped<ISubscriptionRepository>(_ => fakeRepo);
        services.AddScoped<IMediator>(_ => fakeMediator);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var worker = new SubscriptionBillingWorker(scopeFactory, NullLogger<SubscriptionBillingWorker>.Instance);

        var result = await worker.ProcessBillingCycleAsync(CancellationToken.None);

        result.Should().Be(0);
        fakeMediator.SentCommands.Should().BeEmpty();
    }

    private sealed class FakeSubscriptionRepository(List<Subscription> dueSubscriptions) : ISubscriptionRepository
    {
        public Task<Subscription?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(dueSubscriptions.FirstOrDefault(s => s.Id == id));

        public Task<IReadOnlyList<Subscription>> ListByPartyIdAsync(string partyId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Subscription>>(dueSubscriptions.Where(s => s.PartyId == partyId).ToList());

        public Task<IReadOnlyList<Subscription>> ListDueSubscriptionsAsync(DateTimeOffset asOf, int limit = 50, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Subscription>>(dueSubscriptions.Where(s => s.NextBillingAt <= asOf).Take(limit).ToList());

        public Task<IReadOnlyList<Subscription>> ListAsync(int skip = 0, int take = 50, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Subscription>>(dueSubscriptions.Skip(skip).Take(take).ToList());

        public Task SaveAsync(Subscription subscription, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeMediator(PaymentDto response) : IMediator
    {
        public List<AuthorizePaymentCommand> SentCommands { get; } = [];

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is AuthorizePaymentCommand cmd)
            {
                SentCommands.Add(cmd);
                return Task.FromResult((TResponse)(object)response);
            }

            throw new NotImplementedException();
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest =>
            throw new NotImplementedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task Publish(object notification, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification =>
            Task.CompletedTask;
    }
}
