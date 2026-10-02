using FluentAssertions;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Events;

namespace PaymentGateway.UnitTests.Domain;

public sealed class PaymentLifecycleDomainTests
{
    [Fact]
    public void Authorize_RaisesPaymentTransitionDomainEvent_WithAuthorizedOutcome()
    {
        var payment = Payment.Authorize(
            partyId: "party_cust_999",
            amount: 5000L,
            currency: "EUR",
            settlementChannel: "ADYEN",
            channelReference: "adyen_tx_123");

        payment.Version.Should().Be(1);
        payment.DomainEvents.Should().HaveCount(1);

        var domainEvt = payment.DomainEvents.Single().Should().BeOfType<PaymentTransitionDomainEvent>().Subject;
        domainEvt.PaymentId.Should().Be(payment.Id);
        domainEvt.PartyId.Should().Be("party_cust_999");
        domainEvt.Outcome.Should().Be(PaymentLifecycleOutcome.Authorized);
        domainEvt.Amount.Should().Be(5000L);
        domainEvt.Currency.Should().Be("EUR");
        domainEvt.Version.Should().Be(1);

        payment.ClearDomainEvents();
        payment.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Decline_RaisesPaymentTransitionDomainEvent_WithDeclinedOutcome()
    {
        var payment = Payment.Decline(
            partyId: "party_cust_888",
            amount: 2500L,
            currency: "USD",
            settlementChannel: "ADYEN",
            channelReference: "adyen_tx_refused",
            declineReason: "Refused");

        payment.Version.Should().Be(1);
        payment.DomainEvents.Should().HaveCount(1);

        var domainEvt = payment.DomainEvents.Single().Should().BeOfType<PaymentTransitionDomainEvent>().Subject;
        domainEvt.PaymentId.Should().Be(payment.Id);
        domainEvt.PartyId.Should().Be("party_cust_888");
        domainEvt.Outcome.Should().Be(PaymentLifecycleOutcome.Declined);
        domainEvt.Amount.Should().Be(2500L);
        domainEvt.Currency.Should().Be("USD");
        domainEvt.Version.Should().Be(1);
    }
}
