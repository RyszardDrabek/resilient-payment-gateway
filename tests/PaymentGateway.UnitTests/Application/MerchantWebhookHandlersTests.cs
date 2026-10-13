using FluentAssertions;
using PaymentGateway.Application.Webhooks;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Ports;
using Xunit;

namespace PaymentGateway.UnitTests.Application;

public sealed class MerchantWebhookHandlersTests
{
    private sealed class InMemorySubscriptionRepository : IMerchantWebhookSubscriptionRepository
    {
        public readonly Dictionary<string, MerchantWebhookSubscription> Subscriptions = [];

        public Task<MerchantWebhookSubscription?> GetActiveByPartyIdAsync(string partyId, CancellationToken ct = default)
        {
            Subscriptions.TryGetValue(partyId, out var sub);
            return Task.FromResult(sub is not null && sub.IsActive ? sub : null);
        }

        public Task<MerchantWebhookSubscription?> GetByPartyIdAsync(string partyId, CancellationToken ct = default)
        {
            Subscriptions.TryGetValue(partyId, out var sub);
            return Task.FromResult(sub);
        }

        public Task AddAsync(MerchantWebhookSubscription subscription, CancellationToken ct = default)
        {
            Subscriptions[subscription.PartyId] = subscription;
            return Task.CompletedTask;
        }

        public Task UpsertAsync(MerchantWebhookSubscription subscription, CancellationToken ct = default)
        {
            Subscriptions[subscription.PartyId] = subscription;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSimulator : IMerchantWebhookSimulator
    {
        public string? CapturedEndpointUrl { get; private set; }
        public string? CapturedSecretKey { get; private set; }
        public string? CapturedEventType { get; private set; }

        public Task<TestMerchantWebhookDispatchResultDto> SimulateDispatchAsync(
            string endpointUrl,
            string secretKey,
            string? eventType = null,
            CancellationToken ct = default)
        {
            CapturedEndpointUrl = endpointUrl;
            CapturedSecretKey = secretKey;
            CapturedEventType = eventType;

            return Task.FromResult(new TestMerchantWebhookDispatchResultDto(
                Success: true,
                StatusCode: 200,
                LatencyMs: 42,
                EndpointUrl: endpointUrl,
                EventId: "evt_test",
                Timestamp: "1234567890",
                Signature: "fake_signature",
                RequestHeaders: new Dictionary<string, string> { ["X-Signature-SHA256"] = "fake_signature" },
                RequestPayload: "{}",
                ResponseBody: "ok",
                ErrorMessage: null));
        }
    }

    [Fact]
    public async Task GetQueryHandler_ReturnsNull_WhenSubscriptionDoesNotExist()
    {
        var repo = new InMemorySubscriptionRepository();
        var handler = new GetMerchantWebhookSubscriptionQueryHandler(repo);

        var result = await handler.Handle(new GetMerchantWebhookSubscriptionQuery("merch_unknown"), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetQueryHandler_ReturnsDto_WhenSubscriptionExists()
    {
        var repo = new InMemorySubscriptionRepository();
        var sub = new MerchantWebhookSubscription(Guid.NewGuid(), "merch_1", "https://example.com/hook", "secret_123", isActive: true);
        await repo.AddAsync(sub);

        var handler = new GetMerchantWebhookSubscriptionQueryHandler(repo);
        var result = await handler.Handle(new GetMerchantWebhookSubscriptionQuery("merch_1"), CancellationToken.None);

        result.Should().NotBeNull();
        result!.PartyId.Should().Be("merch_1");
        result.EndpointUrl.Should().Be("https://example.com/hook");
        result.SecretKey.Should().Be("secret_123");
        result.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateCommandHandler_CreatesNewSubscription_WhenNotExisting()
    {
        var repo = new InMemorySubscriptionRepository();
        var handler = new UpdateMerchantWebhookSubscriptionCommandHandler(repo);

        var result = await handler.Handle(
            new UpdateMerchantWebhookSubscriptionCommand("merch_2", "https://api.acme.com/v1", true),
            CancellationToken.None);

        result.PartyId.Should().Be("merch_2");
        result.EndpointUrl.Should().Be("https://api.acme.com/v1");
        result.IsActive.Should().BeTrue();
        result.SecretKey.Should().StartWith("whsec_");
        repo.Subscriptions.Should().ContainKey("merch_2");
    }

    [Fact]
    public async Task UpdateCommandHandler_ModifiesExistingSubscription()
    {
        var repo = new InMemorySubscriptionRepository();
        var initial = new MerchantWebhookSubscription(Guid.NewGuid(), "merch_3", "https://old.com/hook", "whsec_old", isActive: true);
        await repo.AddAsync(initial);

        var handler = new UpdateMerchantWebhookSubscriptionCommandHandler(repo);
        var result = await handler.Handle(
            new UpdateMerchantWebhookSubscriptionCommand("merch_3", "https://new.com/hook", false),
            CancellationToken.None);

        result.EndpointUrl.Should().Be("https://new.com/hook");
        result.IsActive.Should().BeFalse();
        result.SecretKey.Should().Be("whsec_old");
    }

    [Fact]
    public async Task RotateSecretCommandHandler_GeneratesNewKeyAndPreservesEndpoint()
    {
        var repo = new InMemorySubscriptionRepository();
        var initial = new MerchantWebhookSubscription(Guid.NewGuid(), "merch_4", "https://acme.com/hook", "whsec_first", isActive: true);
        await repo.AddAsync(initial);

        var handler = new RotateMerchantWebhookSecretCommandHandler(repo);
        var result = await handler.Handle(new RotateMerchantWebhookSecretCommand("merch_4"), CancellationToken.None);

        result.SecretKey.Should().NotBe("whsec_first");
        result.SecretKey.Should().StartWith("whsec_");
        result.EndpointUrl.Should().Be("https://acme.com/hook");
    }

    [Fact]
    public async Task RotateSecretCommandHandler_Throws_WhenSubscriptionDoesNotExist()
    {
        var repo = new InMemorySubscriptionRepository();
        var handler = new RotateMerchantWebhookSecretCommandHandler(repo);

        var act = () => handler.Handle(new RotateMerchantWebhookSecretCommand("merch_nonexistent"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no subscription exists*");
    }

    [Fact]
    public async Task TestDispatchCommandHandler_Throws_WhenNoEndpointAvailable()
    {
        var repo = new InMemorySubscriptionRepository();
        var simulator = new FakeSimulator();
        var handler = new TestMerchantWebhookDispatchCommandHandler(repo, simulator);

        var act = () => handler.Handle(new TestMerchantWebhookDispatchCommand("merch_no_endpoint"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No webhook endpoint configured*");
    }

    [Fact]
    public async Task TestDispatchCommandHandler_DelegatesToSimulator_WithConfiguredValues()
    {
        var repo = new InMemorySubscriptionRepository();
        var sub = new MerchantWebhookSubscription(Guid.NewGuid(), "merch_5", "https://target.com/hook", "whsec_key5", isActive: true);
        await repo.AddAsync(sub);

        var simulator = new FakeSimulator();
        var handler = new TestMerchantWebhookDispatchCommandHandler(repo, simulator);

        var result = await handler.Handle(
            new TestMerchantWebhookDispatchCommand("merch_5", null, "payment.authorized"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        simulator.CapturedEndpointUrl.Should().Be("https://target.com/hook");
        simulator.CapturedSecretKey.Should().Be("whsec_key5");
        simulator.CapturedEventType.Should().Be("payment.authorized");
    }
}
