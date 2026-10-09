using FluentAssertions;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.Events;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Infrastructure.Repositories;
using PaymentGateway.Infrastructure.Risk;
using Xunit;

namespace PaymentGateway.UnitTests.Infrastructure;

public sealed class DeterministicRiskScorerTests
{
    [Fact]
    public async Task ScoreAsync_NormalAmountWithinHistory_ReturnsClean()
    {
        var options = Options.Create(new RiskOptions { AnomalyAmountMultiplier = 5.0 });
        var scorer = new DeterministicRiskScorer(options);

        var history = new List<PartyRiskContext>
        {
            new("c1", "party-1", "p0", "e0", 1000, "EUR", [1f], DateTimeOffset.UtcNow.AddMinutes(-10)),
            new("c2", "party-1", "p1", "e1", 1200, "EUR", [1f], DateTimeOffset.UtcNow.AddMinutes(-5))
        };

        var evt = new PaymentLifecycleEvent("e2", "p2", "party-1", "Authorized", 1500, "EUR", 1, DateTimeOffset.UtcNow);
        var result = await scorer.ScoreAsync(evt, history, history);

        result.Status.Should().Be(RiskVerdictStatus.Clean);
        result.IsAnomalous.Should().BeFalse();
        result.Reason.Should().Contain("Normal transaction");
    }

    [Fact]
    public async Task ScoreAsync_AmountSpikeAboveMultiplier_ReturnsAnomalous()
    {
        var options = Options.Create(new RiskOptions { AnomalyAmountMultiplier = 5.0 });
        var scorer = new DeterministicRiskScorer(options);

        var history = new List<PartyRiskContext>
        {
            new("c1", "party-1", "p0", "e0", 1000, "EUR", [1f], DateTimeOffset.UtcNow.AddMinutes(-10)),
            new("c2", "party-1", "p1", "e1", 1000, "EUR", [1f], DateTimeOffset.UtcNow.AddMinutes(-5))
        };

        var evt = new PaymentLifecycleEvent("e2", "p2", "party-1", "Authorized", 10000, "EUR", 1, DateTimeOffset.UtcNow); // 10x historical average
        var result = await scorer.ScoreAsync(evt, history, history);

        result.Status.Should().Be(RiskVerdictStatus.Anomalous);
        result.IsAnomalous.Should().BeTrue();
        result.Reason.Should().Contain("historical average");
    }

    [Fact]
    public void ComputeCosineSimilarity_IdenticalVectors_ReturnsOne()
    {
        float[] a = [1f, 2f, 3f];
        float[] b = [1f, 2f, 3f];

        var sim = PartyRiskContextStore.ComputeCosineSimilarity(a, b);
        sim.Should().BeApproximately(1.0f, 0.001f);
    }

    [Fact]
    public void ComputeCosineSimilarity_OrthogonalVectors_ReturnsZero()
    {
        float[] a = [1f, 0f];
        float[] b = [0f, 1f];

        var sim = PartyRiskContextStore.ComputeCosineSimilarity(a, b);
        sim.Should().BeApproximately(0.0f, 0.001f);
    }
}
