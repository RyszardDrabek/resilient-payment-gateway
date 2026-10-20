using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.Risk.Models;
using PaymentGateway.Application.Risk.Ports;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Infrastructure.Risk;
using Xunit;

namespace PaymentGateway.UnitTests.Infrastructure;

public sealed class InlineRiskEvaluatorTests : IDisposable
{
    private class StubRiskContextStore : IRiskContextStore
    {
        public Func<string, Task<IReadOnlyList<PartyRiskContext>>>? Handler { get; set; }

        public Task<IReadOnlyList<PartyRiskContext>> GetPartyHistoryAsync(string partyId, CancellationToken ct = default)
        {
            if (Handler is not null) return Handler(partyId);
            return Task.FromResult<IReadOnlyList<PartyRiskContext>>([]);
        }

        public Task RecordContextAsync(PartyRiskContext context, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<PartyRiskContext>> FindSimilarContextsAsync(string partyId, float[] targetEmbedding, int limit = 5, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PartyRiskContext>>([]);
    }

    public InlineRiskEvaluatorTests()
    {
        InlineRiskEvaluator.ResetVelocity();
    }

    public void Dispose()
    {
        InlineRiskEvaluator.ResetVelocity();
    }

    [Fact]
    public async Task EvaluateAsync_WhenDisabled_ReturnsAllowImmediately()
    {
        var options = Options.Create(new InlineRiskOptions { Enabled = false });
        var evaluator = new InlineRiskEvaluator(options);

        var result = await evaluator.EvaluateAsync("party_fraud_001", 1000, "EUR");

        result.Decision.Should().Be(InlineRiskDecision.Allow);
        result.RiskScore.Should().Be(0);
        result.Reason.Should().Contain("disabled");
        result.IsDegraded.Should().BeFalse();
    }

    [Theory]
    [InlineData("party_fraud_attacker")]
    [InlineData("fraud_bot_99")]
    [InlineData("party_blocked_corp")]
    [InlineData("blocked_carding")]
    public async Task EvaluateAsync_WhenPartyIsBlacklisted_ReturnsBlockUpfront(string blacklistedParty)
    {
        var options = Options.Create(new InlineRiskOptions());
        var evaluator = new InlineRiskEvaluator(options);

        var result = await evaluator.EvaluateAsync(blacklistedParty, 2500, "EUR");

        result.Decision.Should().Be(InlineRiskDecision.Block);
        result.RiskScore.Should().Be(95);
        result.Reason.Should().Contain("Known fraudulent party pattern");
        result.IsDegraded.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_WhenVelocityLimitExceeded_ReturnsBlock()
    {
        var options = Options.Create(new InlineRiskOptions { MaxVelocityPerMinute = 3 });
        var evaluator = new InlineRiskEvaluator(options);

        var partyId = "velocity_target_" + Guid.NewGuid();

        // 3 allowed calls
        for (var i = 0; i < 3; i++)
        {
            var res = await evaluator.EvaluateAsync(partyId, 100, "EUR");
            res.Decision.Should().Be(InlineRiskDecision.Allow);
        }

        // 4th call exceeds limit
        var blockedRes = await evaluator.EvaluateAsync(partyId, 100, "EUR");
        blockedRes.Decision.Should().Be(InlineRiskDecision.Block);
        blockedRes.RiskScore.Should().Be(88);
        blockedRes.Reason.Should().Contain("Velocity limit exceeded");
    }

    [Fact]
    public async Task EvaluateAsync_WhenPartyDemandsChallenge_ReturnsChallenge3Ds()
    {
        var options = Options.Create(new InlineRiskOptions());
        var evaluator = new InlineRiskEvaluator(options);

        var result = await evaluator.EvaluateAsync("party_challenge_required", 5000, "EUR");

        result.Decision.Should().Be(InlineRiskDecision.Challenge3Ds);
        result.RiskScore.Should().Be(70);
        result.Reason.Should().Contain("3DS2 Challenge");
    }

    [Fact]
    public async Task EvaluateAsync_WhenHighValueTransaction_ReturnsChallenge3Ds()
    {
        var options = Options.Create(new InlineRiskOptions());
        var evaluator = new InlineRiskEvaluator(options);

        var result = await evaluator.EvaluateAsync("legit_merchant", 10_000_000, "EUR");

        result.Decision.Should().Be(InlineRiskDecision.Challenge3Ds);
        result.RiskScore.Should().Be(65);
        result.Reason.Should().Contain("exceeds frictionless threshold");
    }

    [Fact]
    public async Task EvaluateAsync_WhenAnomalyAboveMultiplier_ReturnsChallenge3Ds()
    {
        var contextStore = new StubRiskContextStore
        {
            Handler = _ => Task.FromResult<IReadOnlyList<PartyRiskContext>>([
                new("c1", "spike_user", "p1", "e1", 1000, "EUR", [1f], DateTimeOffset.UtcNow.AddMinutes(-5)),
                new("c2", "spike_user", "p2", "e2", 1200, "EUR", [1f], DateTimeOffset.UtcNow.AddMinutes(-2))
            ])
        };

        var options = Options.Create(new InlineRiskOptions());
        var evaluator = new InlineRiskEvaluator(options, contextStore);

        // 1100 avg * 6x = 6600
        var result = await evaluator.EvaluateAsync("spike_user", 7000, "EUR");

        result.Decision.Should().Be(InlineRiskDecision.Challenge3Ds);
        result.RiskScore.Should().Be(62);
        result.Reason.Should().Contain("historical average");
    }

    [Fact]
    public async Task EvaluateAsync_WhenNormalTransaction_ReturnsAllow()
    {
        var options = Options.Create(new InlineRiskOptions());
        var evaluator = new InlineRiskEvaluator(options);

        var result = await evaluator.EvaluateAsync("clean_shopper_01", 4500, "EUR");

        result.Decision.Should().Be(InlineRiskDecision.Allow);
        result.RiskScore.Should().Be(10);
        result.Reason.Should().Contain("Low-risk transaction");
        result.IsDegraded.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_WhenContextStoreThrows_FailsOpenWithAllow()
    {
        var contextStore = new StubRiskContextStore
        {
            Handler = _ => throw new InvalidOperationException("Postgres connection dropped")
        };

        var options = Options.Create(new InlineRiskOptions());
        var evaluator = new InlineRiskEvaluator(options, contextStore, NullLogger<InlineRiskEvaluator>.Instance);

        var result = await evaluator.EvaluateAsync("some_shopper", 5000, "EUR");

        result.Decision.Should().Be(InlineRiskDecision.Allow);
        result.IsDegraded.Should().BeTrue();
        result.Reason.Should().Contain("Fail-open");
    }
}
