using FluentAssertions;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using Xunit;

namespace PaymentGateway.UnitTests.Domain;

public sealed class RiskDomainTests
{
    [Fact]
    public void CleanVerdict_ShouldHaveCleanStatus_AndNoFlag()
    {
        var scoredAt = DateTimeOffset.UtcNow;
        var verdict = RiskVerdict.Clean("v1", "e1", "p1", "party-1", "all good", scoredAt);

        verdict.Status.Should().Be(RiskVerdictStatus.Clean);
        verdict.EventId.Should().Be("e1");
        verdict.PaymentId.Should().Be("p1");
        verdict.PartyId.Should().Be("party-1");
        verdict.FlagId.Should().BeNull();
        verdict.Reason.Should().Be("all good");
    }

    [Fact]
    public void AnomalousVerdict_ShouldReferenceFlag()
    {
        var scoredAt = DateTimeOffset.UtcNow;
        var flag = new RiskFlag("flag-1", "p1", "party-1", "e1", "High velocity and high amount deviation", scoredAt);
        var verdict = RiskVerdict.Anomalous("v1", "e1", "p1", "party-1", flag.Reason, flag.Id, scoredAt);

        verdict.Status.Should().Be(RiskVerdictStatus.Anomalous);
        verdict.FlagId.Should().Be("flag-1");
        verdict.Reason.Should().Contain("velocity");
        flag.Status.Should().Be("Open");
    }

    [Fact]
    public void DegradedVerdict_ShouldRecordDegradedStatus()
    {
        var scoredAt = DateTimeOffset.UtcNow;
        var verdict = RiskVerdict.Degraded("v1", "e1", "p1", "party-1", "Timeout scoring party context", scoredAt);

        verdict.Status.Should().Be(RiskVerdictStatus.Degraded);
        verdict.Reason.Should().Be("Timeout scoring party context");
        verdict.FlagId.Should().BeNull();
    }
}
