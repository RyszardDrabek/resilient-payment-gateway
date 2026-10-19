using FluentAssertions;
using PaymentGateway.Domain.Entities;
using Xunit;

namespace PaymentGateway.UnitTests.Domain;

public sealed class SubscriptionTests
{
    [Fact]
    public void Constructor_WithValidArguments_InitializesActiveSubscription()
    {
        var start = DateTimeOffset.UtcNow;
        var sub = new Subscription(
            "sub_123",
            "party_abc",
            "pm_123",
            2500,
            "EUR",
            SubscriptionInterval.Monthly,
            startDate: start);

        sub.Id.Should().Be("sub_123");
        sub.PartyId.Should().Be("party_abc");
        sub.PaymentMethodId.Should().Be("pm_123");
        sub.Amount.Should().Be(2500);
        sub.Currency.Should().Be("EUR");
        sub.Interval.Should().Be(SubscriptionInterval.Monthly);
        sub.State.Should().Be(SubscriptionState.Active);
        sub.NextBillingAt.Should().Be(start);
        sub.FailureCount.Should().Be(0);
        sub.LastBilledAt.Should().BeNull();
        sub.CancelledAt.Should().BeNull();
    }

    [Fact]
    public void PauseAndResume_TransitionsStateAccurately()
    {
        var sub = new Subscription("sub_1", "party_1", "pm_1", 1000, "EUR", SubscriptionInterval.Monthly);

        sub.Pause();
        sub.State.Should().Be(SubscriptionState.Paused);

        // Idempotent pause
        sub.Pause();
        sub.State.Should().Be(SubscriptionState.Paused);

        var now = DateTimeOffset.UtcNow.AddMonths(2);
        sub.Resume(now);
        sub.State.Should().Be(SubscriptionState.Active);
        sub.NextBillingAt.Should().BeOnOrAfter(now);
    }

    [Fact]
    public void Cancel_TransitionsToCancelled_AndBlocksFurtherOperations()
    {
        var sub = new Subscription("sub_1", "party_1", "pm_1", 1000, "EUR", SubscriptionInterval.Monthly);
        var cancelTime = DateTimeOffset.UtcNow;

        sub.Cancel(cancelTime);
        sub.State.Should().Be(SubscriptionState.Cancelled);
        sub.CancelledAt.Should().Be(cancelTime);

        // Cannot pause or resume
        var actPause = () => sub.Pause();
        actPause.Should().Throw<InvalidOperationException>();

        var actResume = () => sub.Resume();
        actResume.Should().Throw<InvalidOperationException>();

        var actBilling = () => sub.RecordBillingSuccess(DateTimeOffset.UtcNow);
        actBilling.Should().Throw<InvalidOperationException>();

        var actFailure = () => sub.RecordBillingFailure();
        actFailure.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RecordBillingSuccess_AdvancesNextBilling_AndResetsFailureCount()
    {
        var sub = new Subscription("sub_1", "party_1", "pm_1", 1000, "EUR", SubscriptionInterval.Monthly);
        sub.RecordBillingFailure();
        sub.FailureCount.Should().Be(1);

        var billedAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        sub.RecordBillingSuccess(billedAt);

        sub.FailureCount.Should().Be(0);
        sub.LastBilledAt.Should().Be(billedAt);
        sub.NextBillingAt.Should().Be(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero));
        sub.State.Should().Be(SubscriptionState.Active);
    }

    [Fact]
    public void RecordBillingFailure_IncrementsFailure_AndTransitionsToPastDueAfterThreshold()
    {
        var sub = new Subscription("sub_1", "party_1", "pm_1", 1000, "EUR", SubscriptionInterval.Monthly);

        sub.RecordBillingFailure(maxRetries: 3);
        sub.FailureCount.Should().Be(1);
        sub.State.Should().Be(SubscriptionState.Active);

        sub.RecordBillingFailure(maxRetries: 3);
        sub.FailureCount.Should().Be(2);
        sub.State.Should().Be(SubscriptionState.Active);

        sub.RecordBillingFailure(maxRetries: 3);
        sub.FailureCount.Should().Be(3);
        sub.State.Should().Be(SubscriptionState.PastDue);
    }

    [Theory]
    [InlineData(SubscriptionInterval.Daily, 1)]
    [InlineData(SubscriptionInterval.Weekly, 7)]
    public void CalculateNextBilling_ComputesExpectedInterval(SubscriptionInterval interval, int expectedDays)
    {
        var from = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var next = Subscription.CalculateNextBilling(from, interval);
        next.Should().Be(from.AddDays(expectedDays));
    }
}
