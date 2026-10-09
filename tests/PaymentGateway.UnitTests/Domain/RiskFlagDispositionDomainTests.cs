using FluentAssertions;
using PaymentGateway.Domain.Entities;
using Xunit;

namespace PaymentGateway.UnitTests.Domain;

public sealed class RiskFlagDispositionDomainTests
{
    [Fact]
    public void RiskFlag_InitialState_IsOpen_AndNotDispositioned()
    {
        var raisedAt = DateTimeOffset.UtcNow;
        var flag = new RiskFlag("flag-1", "p1", "party-1", "e1", "Anomalous activity", raisedAt);

        flag.Status.Should().Be("Open");
        flag.Disposition.Should().BeNull();
        flag.DispositionedAt.Should().BeNull();
        flag.IsDispositioned.Should().BeFalse();
    }

    [Theory]
    [InlineData("confirmed")]
    [InlineData("false_positive")]
    [InlineData("escalated")]
    [InlineData("CONFIRMED")]
    [InlineData("False_Positive")]
    [InlineData("ESCALATED")]
    public void RiskFlag_ApplyDisposition_SupportedValues_SetsDispositionAndStatus(string disposition)
    {
        var raisedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        var dispositionedAt = DateTimeOffset.UtcNow;
        var flag = new RiskFlag("flag-1", "p1", "party-1", "e1", "Anomalous activity", raisedAt);

        flag.ApplyDisposition(disposition, dispositionedAt);

        flag.Status.Should().Be("Dispositioned");
        flag.Disposition.Should().Be(disposition.ToLowerInvariant());
        flag.DispositionedAt.Should().Be(dispositionedAt);
        flag.IsDispositioned.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("unknown")]
    [InlineData("approved")]
    [InlineData("rejected")]
    public void RiskFlag_ApplyDisposition_UnsupportedValue_ThrowsArgumentException(string invalidDisposition)
    {
        var raisedAt = DateTimeOffset.UtcNow;
        var flag = new RiskFlag("flag-1", "p1", "party-1", "e1", "Anomalous activity", raisedAt);

        var act = () => flag.ApplyDisposition(invalidDisposition, DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>();
        flag.Status.Should().Be("Open");
        flag.Disposition.Should().BeNull();
        flag.IsDispositioned.Should().BeFalse();
    }

    [Fact]
    public void RiskFlag_ApplyDisposition_AlreadyDispositioned_ThrowsInvalidOperationException()
    {
        var raisedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var flag = new RiskFlag("flag-1", "p1", "party-1", "e1", "Anomalous activity", raisedAt);
        flag.ApplyDisposition("confirmed", DateTimeOffset.UtcNow);

        var act = () => flag.ApplyDisposition("false_positive", DateTimeOffset.UtcNow);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already dispositioned*");
        flag.Disposition.Should().Be("confirmed");
    }

    [Fact]
    public void RiskFlag_ApplyDisposition_WithNotes_SetsReviewerNotes()
    {
        var raisedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var flag = new RiskFlag("flag-1", "p1", "party-1", "e1", "Anomalous activity", raisedAt);

        flag.ApplyDisposition("confirmed", DateTimeOffset.UtcNow, "Fraud team confirmed compromise");

        flag.ReviewerNotes.Should().Be("Fraud team confirmed compromise");
    }

    [Fact]
    public void RiskFlag_ApplyDisposition_WithWhitespaceNotes_SetsNullReviewerNotes()
    {
        var raisedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var flag = new RiskFlag("flag-1", "p1", "party-1", "e1", "Anomalous activity", raisedAt);

        flag.ApplyDisposition("false_positive", DateTimeOffset.UtcNow, "   ");

        flag.ReviewerNotes.Should().BeNull();
    }

    [Fact]
    public void RiskFlag_IsDispositioned_DependsStrictlyOnDispositionPresence()
    {
        var raisedAt = DateTimeOffset.UtcNow;
        var flag = new RiskFlag("flag-1", "p1", "party-1", "e1", "Anomalous activity", raisedAt);

        flag.IsDispositioned.Should().BeFalse();
        flag.ApplyDisposition("escalated", DateTimeOffset.UtcNow);
        flag.IsDispositioned.Should().BeTrue();
    }
}
