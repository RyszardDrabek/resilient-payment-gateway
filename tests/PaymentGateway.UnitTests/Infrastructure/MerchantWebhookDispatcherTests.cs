using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.Events;
using PaymentGateway.Application.Webhooks;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Ports;
using PaymentGateway.Infrastructure.Options;
using PaymentGateway.Infrastructure.Webhooks;
using Xunit;

namespace PaymentGateway.UnitTests.Infrastructure;

public sealed class MerchantWebhookDispatcherTests
{
    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class StubSubscriptionRepository(MerchantWebhookSubscription? subscription) : IMerchantWebhookSubscriptionRepository
    {
        public Task<MerchantWebhookSubscription?> GetActiveByPartyIdAsync(string partyId, CancellationToken ct = default) =>
            Task.FromResult(subscription);

        public Task<MerchantWebhookSubscription?> GetByPartyIdAsync(string partyId, CancellationToken ct = default) =>
            Task.FromResult(subscription);

        public Task AddAsync(MerchantWebhookSubscription sub, CancellationToken ct = default) => Task.CompletedTask;

        public Task UpsertAsync(MerchantWebhookSubscription sub, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(responder(request));
        }
    }

    [Fact]
    public async Task DispatchAsync_WithDbSubscription_UsesSubscriptionEndpointAndKey()
    {
        var subscription = new MerchantWebhookSubscription(
            Guid.NewGuid(),
            "party_test_1",
            "https://merchant-db.example/hook",
            "db_secret_key_123");
        var repo = new StubSubscriptionRepository(subscription);

        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var httpClient = new HttpClient(handler);
        var signer = new HmacSha256WebhookSigner();
        var options = Options.Create(new MerchantWebhookOptions
        {
            DefaultEndpointUrl = "https://default.example/hook",
            DefaultSecretKey = "default_secret",
            MaxRetryAttempts = 1,
            InitialBackoffSeconds = 0
        });

        var fixedTime = new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(fixedTime);

        var dispatcher = new MerchantWebhookDispatcher(
            repo,
            httpClient,
            signer,
            options,
            NullLogger<MerchantWebhookDispatcher>.Instance,
            timeProvider);

        var evt = new PaymentLifecycleEvent(
            EventId: "evt_sub_1",
            PaymentId: "pay_sub_1",
            PartyId: "party_test_1",
            Outcome: "captured",
            Amount: 5000,
            Currency: "EUR",
            Version: 1,
            OccurredAt: fixedTime);

        var result = await dispatcher.DispatchAsync(evt);

        result.Success.Should().BeTrue();
        result.EndpointUrl.Should().Be("https://merchant-db.example/hook");
        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.RequestUri.Should().Be(new Uri("https://merchant-db.example/hook"));
        handler.LastRequest.Headers.GetValues("X-Event-Id").Should().ContainSingle().Which.Should().Be("evt_sub_1");
        handler.LastRequest.Headers.GetValues("X-Timestamp").Should().ContainSingle().Which.Should().Be(fixedTime.ToUnixTimeSeconds().ToString());
        handler.LastRequest.Headers.Contains("X-Signature-SHA256").Should().BeTrue();
    }

    [Fact]
    public async Task DispatchAsync_WithoutDbSubscription_FallsBackToDefaultConfiguration()
    {
        var repo = new StubSubscriptionRepository(null);

        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var httpClient = new HttpClient(handler);
        var signer = new HmacSha256WebhookSigner();
        var options = Options.Create(new MerchantWebhookOptions
        {
            DefaultEndpointUrl = "https://fallback.example/hook",
            DefaultSecretKey = "fallback_secret",
            MaxRetryAttempts = 1,
            InitialBackoffSeconds = 0
        });

        var dispatcher = new MerchantWebhookDispatcher(
            repo,
            httpClient,
            signer,
            options,
            NullLogger<MerchantWebhookDispatcher>.Instance);

        var evt = new PaymentLifecycleEvent(
            EventId: "evt_fallback_1",
            PaymentId: "pay_fallback_1",
            PartyId: "party_unknown",
            Outcome: "refunded",
            Amount: 3000,
            Currency: "EUR",
            Version: 1,
            OccurredAt: DateTimeOffset.UtcNow);

        var result = await dispatcher.DispatchAsync(evt);

        result.Success.Should().BeTrue();
        result.EndpointUrl.Should().Be("https://fallback.example/hook");
        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.RequestUri.Should().Be(new Uri("https://fallback.example/hook"));
    }

    [Fact]
    public async Task DispatchAsync_TransientFailures_RetriesUpToMaxAttempts()
    {
        var repo = new StubSubscriptionRepository(null);

        var attempts = 0;
        var handler = new StubHttpMessageHandler(_ =>
        {
            attempts++;
            return attempts < 3
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : new HttpResponseMessage(HttpStatusCode.OK);
        });

        var httpClient = new HttpClient(handler);
        var signer = new HmacSha256WebhookSigner();
        var options = Options.Create(new MerchantWebhookOptions
        {
            DefaultEndpointUrl = "https://retry.example/hook",
            DefaultSecretKey = "retry_secret",
            MaxRetryAttempts = 5,
            InitialBackoffSeconds = 0
        });

        var dispatcher = new MerchantWebhookDispatcher(
            repo,
            httpClient,
            signer,
            options,
            NullLogger<MerchantWebhookDispatcher>.Instance);

        var evt = new PaymentLifecycleEvent(
            EventId: "evt_retry_1",
            PaymentId: "pay_retry_1",
            PartyId: "party_retry",
            Outcome: "cancelled",
            Amount: 1000,
            Currency: "EUR",
            Version: 1,
            OccurredAt: DateTimeOffset.UtcNow);

        var result = await dispatcher.DispatchAsync(evt);

        result.Success.Should().BeTrue();
        result.DeliveryAttempts.Should().Be(3);
        attempts.Should().Be(3);
    }

    [Fact]
    public async Task DispatchAsync_WhenCancellationTokenCancelled_ThrowsOperationCanceledExceptionDirectly()
    {
        var repo = new StubSubscriptionRepository(null);
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // cancelled prior to or during request

        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var httpClient = new HttpClient(handler);
        var signer = new HmacSha256WebhookSigner();
        var options = Options.Create(new MerchantWebhookOptions
        {
            DefaultEndpointUrl = "https://cancel.example/hook",
            DefaultSecretKey = "cancel_secret",
            MaxRetryAttempts = 5,
            InitialBackoffSeconds = 0
        });

        var dispatcher = new MerchantWebhookDispatcher(
            repo,
            httpClient,
            signer,
            options,
            NullLogger<MerchantWebhookDispatcher>.Instance);

        var evt = new PaymentLifecycleEvent(
            EventId: "evt_cancel_1",
            PaymentId: "pay_cancel_1",
            PartyId: "party_cancel",
            Outcome: "captured",
            Amount: 1000,
            Currency: "EUR",
            Version: 1,
            OccurredAt: DateTimeOffset.UtcNow);

        var act = async () => await dispatcher.DispatchAsync(evt, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
