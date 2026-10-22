using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PaymentGateway.Infrastructure.Fx;
using PaymentGateway.Infrastructure.Options;
using PaymentGateway.Worker;
using Xunit;

namespace PaymentGateway.UnitTests.Worker;

public sealed class FxRateRefreshWorkerTests
{
    [Fact]
    public void FxRateRefreshOptions_HasExpectedDefaultValues()
    {
        var options = new FxRateRefreshOptions();

        options.Enabled.Should().BeTrue();
        options.IntervalSeconds.Should().Be(300);
    }

    [Fact]
    public async Task ProcessCycleAsync_UpdatesBaselineRatesInProvider()
    {
        var provider = new CachedFxRateProvider();
        // Modify a rate first
        provider.SetRateToUsd("EUR", 0.5000m);

        var options = Options.Create(new FxRateRefreshOptions { IntervalSeconds = 60 });
        var worker = new FxRateRefreshWorker(provider, options, NullLogger<FxRateRefreshWorker>.Instance);

        await worker.ProcessCycleAsync(CancellationToken.None);

        var eurToUsd = await provider.GetSpotRateAsync("EUR", "USD");
        eurToUsd.Should().Be(1.0850m);
    }

    [Fact]
    public async Task ProcessCycleAsync_WhenPublishEndpointProvided_PublishesFxRatesRefreshedEvent()
    {
        var provider = new CachedFxRateProvider();
        var fakePublishEndpoint = new FakePublishEndpoint();
        var options = Options.Create(new FxRateRefreshOptions { IntervalSeconds = 60 });
        var worker = new FxRateRefreshWorker(
            provider,
            options,
            NullLogger<FxRateRefreshWorker>.Instance,
            publishEndpoint: fakePublishEndpoint);

        await worker.ProcessCycleAsync(CancellationToken.None);

        fakePublishEndpoint.PublishedEvents.Should().ContainSingle();
        var published = fakePublishEndpoint.PublishedEvents[0] as PaymentGateway.Application.Events.FxRatesRefreshedEvent;
        published.Should().NotBeNull();
        published!.RatesToUsd["EUR"].Should().Be(1.0850m);
    }

    private sealed class FakePublishEndpoint : IPublishEndpoint
    {
        public List<object> PublishedEvents { get; } = [];

        public ConnectHandle ConnectPublishObserver(IPublishObserver observer) => throw new NotImplementedException();

        public Task Publish<T>(T message, CancellationToken cancellationToken = default) where T : class
        {
            PublishedEvents.Add(message);
            return Task.CompletedTask;
        }

        public Task Publish<T>(T message, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class
        {
            PublishedEvents.Add(message);
            return Task.CompletedTask;
        }

        public Task Publish<T>(T message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class
        {
            PublishedEvents.Add(message);
            return Task.CompletedTask;
        }

        public Task Publish(object message, CancellationToken cancellationToken = default)
        {
            PublishedEvents.Add(message);
            return Task.CompletedTask;
        }

        public Task Publish(object message, Type messageType, CancellationToken cancellationToken = default)
        {
            PublishedEvents.Add(message);
            return Task.CompletedTask;
        }

        public Task Publish(object message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default)
        {
            PublishedEvents.Add(message);
            return Task.CompletedTask;
        }

        public Task Publish(object message, Type messageType, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default)
        {
            PublishedEvents.Add(message);
            return Task.CompletedTask;
        }

        public Task Publish<T>(object values, CancellationToken cancellationToken = default) where T : class
        {
            PublishedEvents.Add(values);
            return Task.CompletedTask;
        }

        public Task Publish<T>(object values, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class
        {
            PublishedEvents.Add(values);
            return Task.CompletedTask;
        }

        public Task Publish<T>(object values, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class
        {
            PublishedEvents.Add(values);
            return Task.CompletedTask;
        }
    }
}
