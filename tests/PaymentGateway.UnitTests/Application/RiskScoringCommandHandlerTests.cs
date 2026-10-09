using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PaymentGateway.Application.Events;
using PaymentGateway.Application.Risk.Commands;
using PaymentGateway.Application.Risk.Models;
using PaymentGateway.Application.Risk.Ports;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using Xunit;

namespace PaymentGateway.UnitTests.Application;

public sealed class RiskScoringCommandHandlerTests
{
    private sealed class InMemoryRiskVerdictRepository : IRiskVerdictRepository
    {
        public readonly Dictionary<string, RiskVerdict> ByEventId = [];
        public readonly Dictionary<string, RiskVerdict> ByPaymentId = [];

        public Task<RiskVerdict?> GetByEventIdAsync(string eventId, CancellationToken ct = default) =>
            Task.FromResult(ByEventId.GetValueOrDefault(eventId));

        public Task<RiskVerdict?> GetByPaymentIdAsync(string paymentId, CancellationToken ct = default) =>
            Task.FromResult(ByPaymentId.GetValueOrDefault(paymentId));

        public Task<RiskVerdict?> GetByIdentifierAsync(string identifier, CancellationToken ct = default)
        {
            if (ByEventId.TryGetValue(identifier, out var v1)) return Task.FromResult<RiskVerdict?>(v1);
            if (ByPaymentId.TryGetValue(identifier, out var v2)) return Task.FromResult<RiskVerdict?>(v2);
            return Task.FromResult<RiskVerdict?>(null);
        }

        public Task SaveAsync(RiskVerdict verdict, CancellationToken ct = default)
        {
            ByEventId[verdict.EventId] = verdict;
            ByPaymentId[verdict.PaymentId] = verdict;
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryRiskFlagRepository : IRiskFlagRepository
    {
        public readonly List<RiskFlag> Flags = [];

        public Task<RiskFlag?> GetByIdAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(Flags.FirstOrDefault(f => f.Id == id));

        public Task<IReadOnlyList<RiskFlag>> GetByPaymentIdAsync(string paymentId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RiskFlag>>(Flags.Where(f => f.PaymentId == paymentId).ToList());

        public Task SaveAsync(RiskFlag flag, CancellationToken ct = default)
        {
            Flags.Add(flag);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryRiskContextStore : IRiskContextStore
    {
        public readonly List<PartyRiskContext> Contexts = [];

        public Task RecordContextAsync(PartyRiskContext context, CancellationToken ct = default)
        {
            Contexts.Add(context);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PartyRiskContext>> GetPartyHistoryAsync(string partyId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PartyRiskContext>>(Contexts.Where(c => c.PartyId == partyId).ToList());

        public Task<IReadOnlyList<PartyRiskContext>> FindSimilarContextsAsync(string partyId, float[] targetEmbedding, int limit = 5, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PartyRiskContext>>(Contexts.Where(c => c.PartyId == partyId).Take(limit).ToList());
    }

    private sealed class FakeRiskScorer(RiskScoringResult result) : IRiskScorer
    {
        public float[] ComputeFeatureVector(PaymentLifecycleEvent lifecycleEvent) => [1.0f, 0.0f];

        public Task<RiskScoringResult> ScoreAsync(
            PaymentLifecycleEvent lifecycleEvent,
            IReadOnlyList<PartyRiskContext> historicalContexts,
            IReadOnlyList<PartyRiskContext> similarContexts,
            CancellationToken ct = default) =>
            Task.FromResult(result);
    }

    [Fact]
    public async Task Handle_CleanVerdict_SavesCleanVerdictAndRecordsContext()
    {
        var verdictRepo = new InMemoryRiskVerdictRepository();
        var flagRepo = new InMemoryRiskFlagRepository();
        var contextStore = new InMemoryRiskContextStore();
        var scorer = new FakeRiskScorer(new RiskScoringResult(RiskVerdictStatus.Clean, "Normal transaction", false, [1.0f]));
        var handler = new ScorePaymentLifecycleEventCommandHandler(
            verdictRepo, flagRepo, contextStore, scorer, NullLogger<ScorePaymentLifecycleEventCommandHandler>.Instance);

        var evt = new PaymentLifecycleEvent("e1", "p1", "party-1", "Authorized", 1000, "EUR", 1, DateTimeOffset.UtcNow);
        var verdict = await handler.Handle(new ScorePaymentLifecycleEventCommand(evt), CancellationToken.None);

        verdict.Status.Should().Be(RiskVerdictStatus.Clean);
        verdict.FlagId.Should().BeNull();
        flagRepo.Flags.Should().BeEmpty();
        contextStore.Contexts.Should().ContainSingle(c => c.PaymentId == "p1" && c.PartyId == "party-1");
    }

    [Fact]
    public async Task Handle_AnomalousVerdict_RaisesRiskFlagAndLinksToVerdict()
    {
        var verdictRepo = new InMemoryRiskVerdictRepository();
        var flagRepo = new InMemoryRiskFlagRepository();
        var contextStore = new InMemoryRiskContextStore();
        var scorer = new FakeRiskScorer(new RiskScoringResult(RiskVerdictStatus.Anomalous, "Amount anomaly vs history", true, [1.0f]));
        var handler = new ScorePaymentLifecycleEventCommandHandler(
            verdictRepo, flagRepo, contextStore, scorer, NullLogger<ScorePaymentLifecycleEventCommandHandler>.Instance);

        var evt = new PaymentLifecycleEvent("e2", "p2", "party-1", "Authorized", 999999, "EUR", 1, DateTimeOffset.UtcNow);
        var verdict = await handler.Handle(new ScorePaymentLifecycleEventCommand(evt), CancellationToken.None);

        verdict.Status.Should().Be(RiskVerdictStatus.Anomalous);
        verdict.FlagId.Should().NotBeNullOrEmpty();
        flagRepo.Flags.Should().ContainSingle(f => f.Id == verdict.FlagId && f.PaymentId == "p2");
    }

    [Fact]
    public async Task Handle_ScorerThrows_FailsOpenAndRecordsDegradedVerdict()
    {
        var verdictRepo = new InMemoryRiskVerdictRepository();
        var flagRepo = new InMemoryRiskFlagRepository();
        var contextStore = new InMemoryRiskContextStore();

        var failingScorer = new FailingRiskScorer();
        var handler = new ScorePaymentLifecycleEventCommandHandler(
            verdictRepo, flagRepo, contextStore, failingScorer, NullLogger<ScorePaymentLifecycleEventCommandHandler>.Instance);

        var evt = new PaymentLifecycleEvent("e3", "p3", "party-1", "Authorized", 500, "EUR", 1, DateTimeOffset.UtcNow);
        var verdict = await handler.Handle(new ScorePaymentLifecycleEventCommand(evt), CancellationToken.None);

        verdict.Status.Should().Be(RiskVerdictStatus.Degraded);
        verdict.Reason.Should().Contain("Simulated scoring outage");
        verdictRepo.ByEventId.Should().ContainKey("e3");
        flagRepo.Flags.Should().BeEmpty();
    }

    private sealed class FailingRiskScorer : IRiskScorer
    {
        public float[] ComputeFeatureVector(PaymentLifecycleEvent lifecycleEvent) => throw new InvalidOperationException("Simulated scoring outage");

        public Task<RiskScoringResult> ScoreAsync(
            PaymentLifecycleEvent lifecycleEvent,
            IReadOnlyList<PartyRiskContext> historicalContexts,
            IReadOnlyList<PartyRiskContext> similarContexts,
            CancellationToken ct = default) =>
            throw new InvalidOperationException("Simulated scoring outage");
    }
}
