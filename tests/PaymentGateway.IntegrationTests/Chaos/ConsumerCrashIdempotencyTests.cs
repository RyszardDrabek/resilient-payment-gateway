using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.Events;
using PaymentGateway.Application.Risk.Ports;
using PaymentGateway.Application.Webhooks;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Infrastructure.Consumers;
using PaymentGateway.Infrastructure.Risk;
using Xunit;

namespace PaymentGateway.IntegrationTests.Chaos;

/// <summary>
/// F-CHAOS-03: Verifies consumer idempotency and side-effect deduplication
/// during broker redeliveries or simulated consumer crashes.
/// </summary>
public sealed class ConsumerCrashIdempotencyTests
{
    [Fact]
    public async Task RiskScoringConsumer_RedeliveredEvent_IsStrictlyIdempotentAndProducesSingleVerdict()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(PaymentGateway.Application.Payments.AuthorizePaymentCommand).Assembly));

        var verdictRepo = new InMemoryRiskVerdictRepository();
        var flagRepo = new InMemoryRiskFlagRepository();
        var contextStore = new InMemoryRiskContextStore();
        var riskOptions = Options.Create(new RiskOptions { ScoreTimeoutSeconds = 5 });
        var riskScorer = new DeterministicRiskScorer(riskOptions);

        services.AddSingleton<IRiskVerdictRepository>(verdictRepo);
        services.AddSingleton<IRiskFlagRepository>(flagRepo);
        services.AddSingleton<IRiskContextStore>(contextStore);
        services.AddSingleton<IRiskScorer>(riskScorer);
        services.AddSingleton(riskOptions);

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<MediatR.ISender>();

        var consumer = new PaymentLifecycleRiskConsumer(
            mediator,
            verdictRepo,
            riskOptions,
            NullLogger<PaymentLifecycleRiskConsumer>.Instance);

        var evt = new PaymentLifecycleEvent(
            EventId: $"evt_{Guid.NewGuid():N}",
            PaymentId: $"pay_{Guid.NewGuid():N}",
            PartyId: "party_suspicious_repeat",
            Outcome: "Authorized",
            Amount: 150000,
            Currency: "EUR",
            Version: 1,
            OccurredAt: DateTimeOffset.UtcNow);

        // Act 1: Initial event processing
        var verdict1 = await consumer.ProcessEventAsync(evt);
        verdict1.Should().NotBeNull();

        // Act 2: Simulated broker redelivery after consumer crash / ack timeout
        var verdict2 = await consumer.ProcessEventAsync(evt);
        verdict2.Should().NotBeNull();

        // Assert: Identical verdict returned, no duplicate records persisted
        verdict2.Id.Should().Be(verdict1.Id);
        verdict2.EventId.Should().Be(evt.EventId);

        var persistedVerdict = await verdictRepo.GetByEventIdAsync(evt.EventId);
        persistedVerdict.Should().NotBeNull();
        persistedVerdict!.Id.Should().Be(verdict1.Id);

        var flags = await flagRepo.GetByPaymentIdAsync(evt.PaymentId);
        flags.Count.Should().BeLessThanOrEqualTo(1, "Deduplication must never generate duplicate risk flags for redelivered events");
    }

    [Fact]
    public async Task WebhookDispatcherConsumer_NonTerminalEvents_AreSafelyIgnoredWithoutSideEffects()
    {
        // Arrange
        var mockDispatcher = new MockWebhookDispatcher();
        var consumer = new MerchantWebhookDispatcherConsumer(
            mockDispatcher,
            NullLogger<MerchantWebhookDispatcherConsumer>.Instance);

        var nonTerminalEvent = new PaymentLifecycleEvent(
            EventId: $"evt_{Guid.NewGuid():N}",
            PaymentId: $"pay_{Guid.NewGuid():N}",
            PartyId: "party_webhooks",
            Outcome: "Pending", // Non-terminal outcome
            Amount: 2500,
            Currency: "EUR",
            Version: 1,
            OccurredAt: DateTimeOffset.UtcNow);

        // Act: Deliver event multiple times
        var dispatched1 = await consumer.ProcessEventAsync(nonTerminalEvent);
        var dispatched2 = await consumer.ProcessEventAsync(nonTerminalEvent);

        // Assert
        dispatched1.Should().BeFalse();
        dispatched2.Should().BeFalse();
        mockDispatcher.DispatchedEvents.Should().BeEmpty();
    }

    private sealed class InMemoryRiskVerdictRepository : IRiskVerdictRepository
    {
        private readonly Dictionary<string, RiskVerdict> _byEventId = new();

        public Task<RiskVerdict?> GetByEventIdAsync(string eventId, CancellationToken ct = default)
        {
            _byEventId.TryGetValue(eventId, out var verdict);
            return Task.FromResult(verdict);
        }

        public Task<RiskVerdict?> GetByPaymentIdAsync(string paymentId, CancellationToken ct = default)
        {
            var verdict = _byEventId.Values.FirstOrDefault(v => v.PaymentId == paymentId);
            return Task.FromResult(verdict);
        }

        public Task<RiskVerdict?> GetByIdentifierAsync(string identifier, CancellationToken ct = default)
        {
            var verdict = _byEventId.Values.FirstOrDefault(v => v.Id == identifier || v.EventId == identifier);
            return Task.FromResult(verdict);
        }

        public Task SaveAsync(RiskVerdict verdict, CancellationToken ct = default)
        {
            _byEventId[verdict.EventId] = verdict;
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryRiskFlagRepository : IRiskFlagRepository
    {
        private readonly List<RiskFlag> _flags = [];

        public Task<RiskFlag?> GetByIdAsync(string id, CancellationToken ct = default)
            => Task.FromResult(_flags.FirstOrDefault(f => f.Id == id));

        public Task<IReadOnlyList<RiskFlag>> GetByPaymentIdAsync(string paymentId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RiskFlag>>(_flags.Where(f => f.PaymentId == paymentId).ToList());

        public Task<(IReadOnlyList<RiskFlag> Items, int TotalCount)> GetOpenFlagsAsync(int page = 1, int pageSize = 50, CancellationToken ct = default)
        {
            var open = _flags.Where(f => f.Disposition == null).ToList();
            var items = open.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Task.FromResult(((IReadOnlyList<RiskFlag>)items, open.Count));
        }

        public Task SaveAsync(RiskFlag flag, CancellationToken ct = default)
        {
            _flags.Add(flag);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryRiskContextStore : IRiskContextStore
    {
        private readonly List<PartyRiskContext> _contexts = [];

        public Task RecordContextAsync(PartyRiskContext context, CancellationToken ct = default)
        {
            _contexts.Add(context);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PartyRiskContext>> GetPartyHistoryAsync(string partyId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PartyRiskContext>>(_contexts.Where(c => c.PartyId == partyId).ToList());

        public Task<IReadOnlyList<PartyRiskContext>> FindSimilarContextsAsync(string partyId, float[] targetEmbedding, int limit = 5, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PartyRiskContext>>([]);
    }

    private sealed class MockWebhookDispatcher : IMerchantWebhookDispatcher
    {
        public List<PaymentLifecycleEvent> DispatchedEvents { get; } = [];

        public Task<MerchantWebhookDispatchResult> DispatchAsync(PaymentLifecycleEvent lifecycleEvent, CancellationToken ct = default)
        {
            DispatchedEvents.Add(lifecycleEvent);
            return Task.FromResult(new MerchantWebhookDispatchResult(
                Success: true,
                EndpointUrl: "https://merchant.example.com/webhook",
                PayloadJson: "{}",
                DeliveryAttempts: 1,
                ErrorMessage: null));
        }
    }
}
