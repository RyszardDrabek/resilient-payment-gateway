using FluentAssertions;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Events;

namespace PaymentGateway.UnitTests.Domain;

public sealed class PaymentDomainEventTests
{
    [Fact]
    public void PaymentLifecycleOutcome_HasExactSevenOutcomes()
    {
        var names = Enum.GetNames<PaymentLifecycleOutcome>();
        names.Should().BeEquivalentTo(new[]
        {
            "Authorized",
            "Declined",
            "Captured",
            "Cancelled",
            "Refunded",
            "Pending",
            "Unknown"
        });
    }

    [Fact]
    public void PaymentTransitionDomainEvent_PropertiesSetCorrectly()
    {
        var now = DateTimeOffset.UtcNow;
        var evt = new PaymentTransitionDomainEvent(
            PaymentId: "pay_123",
            PartyId: "party_456",
            Outcome: PaymentLifecycleOutcome.Authorized,
            Amount: 1000L,
            Currency: "EUR",
            Version: 1,
            OccurredAt: now);

        evt.PaymentId.Should().Be("pay_123");
        evt.PartyId.Should().Be("party_456");
        evt.Outcome.Should().Be(PaymentLifecycleOutcome.Authorized);
        evt.Amount.Should().Be(1000L);
        evt.Currency.Should().Be("EUR");
        evt.Version.Should().Be(1);
        evt.OccurredAt.Should().Be(now);
        evt.Should().BeAssignableTo<IDomainEvent>();
    }
}
