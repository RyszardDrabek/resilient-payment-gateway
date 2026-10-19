using System.Diagnostics;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PaymentGateway.Application.Common;
using PaymentGateway.Application.Common.Behaviors;
using PaymentGateway.Application.Events;
using PaymentGateway.Application.Payments;
using PaymentGateway.Application.Webhooks;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Ports;
using PaymentGateway.Infrastructure.Options;
using PaymentGateway.Infrastructure.Persistence;
using PaymentGateway.Infrastructure.Repositories;
using PaymentGateway.Infrastructure.Services;
using PaymentGateway.Infrastructure.Webhooks;
using Xunit;

namespace PaymentGateway.UnitTests.Application;

public sealed class DistributedTracingTests
{
    private sealed record TestSampleCommand(string Id) : IRequest<string>;

    [Fact]
    public async Task TracingPipelineBehavior_CreatesActivityWithMediatrRequestTag()
    {
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == PaymentTracing.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(activityListener);

        Activity? capturedActivity = null;
        var behavior = new TracingPipelineBehavior<TestSampleCommand, string>();
        RequestHandlerDelegate<string> next = _ =>
        {
            capturedActivity = Activity.Current;
            return Task.FromResult("OK");
        };

        var result = await behavior.Handle(
            new TestSampleCommand("cmd_123"),
            next,
            CancellationToken.None);

        result.Should().Be("OK");
        capturedActivity.Should().NotBeNull();
        capturedActivity!.OperationName.Should().Be("MediatR TestSampleCommand");
        capturedActivity.Tags.Should().Contain(t => t.Key == "mediatr.request" && t.Value == "TestSampleCommand");
    }

    [Fact]
    public void PaymentLifecycleEvent_SerializationAndDeserialization_PreservesTraceParent()
    {
        var simulatedTraceId = ActivityTraceId.CreateRandom();
        var simulatedSpanId = ActivitySpanId.CreateRandom();
        var expectedTraceParent = $"00-{simulatedTraceId}-{simulatedSpanId}-01";

        var evt = new PaymentLifecycleEvent(
            EventId: "evt_test_123",
            PaymentId: "pay_test_456",
            PartyId: "merch_789",
            Outcome: "authorized",
            Amount: 10000,
            Currency: "EUR",
            Version: 1,
            OccurredAt: DateTimeOffset.UtcNow,
            TraceParent: expectedTraceParent);

        var json = JsonSerializer.Serialize(evt);
        json.Should().Contain(expectedTraceParent);

        var deserialized = JsonSerializer.Deserialize<PaymentLifecycleEvent>(json);
        deserialized.Should().NotBeNull();
        deserialized!.TraceParent.Should().Be(expectedTraceParent);
        deserialized.PaymentId.Should().Be("pay_test_456");
        deserialized.Outcome.Should().Be("authorized");
    }

    [Fact]
    public async Task MerchantWebhookDispatcher_WhenTraceParentPresent_PropagatesTraceparentHeader()
    {
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == PaymentTracing.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(activityListener);

        HttpRequestMessage? capturedRequest = null;
        var handler = new DelegatingHandlerStub((req, ct) =>
        {
            capturedRequest = req;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        using var httpClient = new HttpClient(handler);
        var repo = new FakeWebhookSubscriptionRepository();
        var signer = new FakeWebhookSigner();

        var options = Microsoft.Extensions.Options.Options.Create(new MerchantWebhookOptions
        {
            DefaultEndpointUrl = "https://merchant.example.com/webhook",
            DefaultSecretKey = "whsec_test123",
            MaxRetryAttempts = 1,
            TimeoutSeconds = 5
        });

        var dispatcher = new MerchantWebhookDispatcher(
            repo,
            httpClient,
            signer,
            options,
            NullLogger<MerchantWebhookDispatcher>.Instance);

        var simulatedTraceId = ActivityTraceId.CreateRandom();
        var simulatedSpanId = ActivitySpanId.CreateRandom();
        var simulatedTraceParent = $"00-{simulatedTraceId}-{simulatedSpanId}-01";

        var evt = new PaymentLifecycleEvent(
            EventId: "evt_webhook_trace_1",
            PaymentId: "pay_webhook_trace_1",
            PartyId: "merchant_test",
            Outcome: "captured",
            Amount: 2500,
            Currency: "EUR",
            Version: 2,
            OccurredAt: DateTimeOffset.UtcNow,
            TraceParent: simulatedTraceParent);

        var result = await dispatcher.DispatchAsync(evt, CancellationToken.None);

        result.Success.Should().BeTrue();
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Headers.Should().Contain(h => h.Key == "traceparent");
        var headerValue = capturedRequest.Headers.GetValues("traceparent").First();
        headerValue.Should().StartWith($"00-{simulatedTraceId}-");
    }

    [Fact]
    public async Task TracingPipelineBehavior_WhenNextThrows_SetsActivityStatusError()
    {
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == PaymentTracing.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(activityListener);

        Activity? capturedActivity = null;
        var behavior = new TracingPipelineBehavior<TestSampleCommand, string>();
        RequestHandlerDelegate<string> next = _ =>
        {
            capturedActivity = Activity.Current;
            throw new InvalidOperationException("Simulated pipeline failure");
        };

        var act = () => behavior.Handle(new TestSampleCommand("cmd_err"), next, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();

        capturedActivity.Should().NotBeNull();
        capturedActivity!.Status.Should().Be(ActivityStatusCode.Error);
        capturedActivity.StatusDescription.Should().Be("Simulated pipeline failure");
        capturedActivity.Events.Should().Contain(e => e.Name == "exception");
    }

    [Fact]
    public async Task MerchantWebhookDispatcher_WhenExhausted_SetsActivityStatusError()
    {
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == PaymentTracing.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(activityListener);

        Activity? capturedActivity = null;
        var handler = new DelegatingHandlerStub((req, ct) =>
        {
            capturedActivity = Activity.Current;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        });

        using var httpClient = new HttpClient(handler);
        var repo = new FakeWebhookSubscriptionRepository();
        var signer = new FakeWebhookSigner();

        var options = Microsoft.Extensions.Options.Options.Create(new MerchantWebhookOptions
        {
            DefaultEndpointUrl = "https://merchant.example.com/webhook",
            DefaultSecretKey = "whsec_test123",
            MaxRetryAttempts = 1,
            TimeoutSeconds = 5
        });

        var dispatcher = new MerchantWebhookDispatcher(
            repo,
            httpClient,
            signer,
            options,
            NullLogger<MerchantWebhookDispatcher>.Instance);

        var evt = new PaymentLifecycleEvent(
            EventId: "evt_err",
            PaymentId: "pay_err",
            PartyId: "merchant_err",
            Outcome: "captured",
            Amount: 1000,
            Currency: "EUR",
            Version: 1,
            OccurredAt: DateTimeOffset.UtcNow);

        var result = await dispatcher.DispatchAsync(evt, CancellationToken.None);

        result.Success.Should().BeFalse();
        capturedActivity.Should().NotBeNull();
        capturedActivity!.Status.Should().Be(ActivityStatusCode.Error);
    }

    private sealed class DelegatingHandlerStub(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }

    private sealed class FakePublishEndpoint : IPublishEndpoint
    {
        public Action<PaymentLifecycleEvent>? OnPublish { get; set; }

        public Task Publish<T>(T message, CancellationToken cancellationToken = default) where T : class
        {
            if (message is PaymentLifecycleEvent evt)
            {
                OnPublish?.Invoke(evt);
            }
            return Task.CompletedTask;
        }

        public Task Publish<T>(T message, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
        public Task Publish<T>(T message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
        public Task Publish(object message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish(object message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish(object message, Type messageType, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish(object message, Type messageType, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<T>(object values, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
        public Task Publish<T>(object values, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
        public Task Publish<T>(object values, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
        public ConnectHandle ConnectPublishObserver(IPublishObserver observer) => throw new NotImplementedException();
    }

    private sealed class FakeWebhookSubscriptionRepository : IMerchantWebhookSubscriptionRepository
    {
        public Task<MerchantWebhookSubscription?> GetActiveByPartyIdAsync(string partyId, CancellationToken ct = default) =>
            Task.FromResult<MerchantWebhookSubscription?>(null);

        public Task<MerchantWebhookSubscription?> GetByPartyIdAsync(string partyId, CancellationToken ct = default) =>
            Task.FromResult<MerchantWebhookSubscription?>(null);

        public Task AddAsync(MerchantWebhookSubscription subscription, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task UpsertAsync(MerchantWebhookSubscription subscription, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeWebhookSigner : IMerchantWebhookSigner
    {
        public string ComputeSignature(string payloadJson, string secretKey) => "fake_signature";
        public bool VerifySignature(string payloadJson, string secretKey, string expectedSignature) => true;
    }
}
