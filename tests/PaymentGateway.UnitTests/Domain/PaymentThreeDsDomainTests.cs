using FluentAssertions;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Events;
using PaymentGateway.Domain.Exceptions;
using Xunit;

namespace PaymentGateway.UnitTests.Domain;

public sealed class PaymentThreeDsDomainTests
{
    [Fact]
    public void CreateRequiresAction_SetsStateAndActionDetails_AndEmitsDomainEvent()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(15);
        var actionDetails = new ThreeDsActionDetails("redirect", "https://bank.example.com/3ds", "token_123", expiresAt);

        var payment = Payment.CreateRequiresAction(
            "merch_acme",
            10000,
            "EUR",
            "mocked-adyen",
            "ref_action_1",
            actionDetails);

        payment.State.Should().Be(PaymentState.RequiresAction);
        payment.Amount.Should().Be(10000);
        payment.Currency.Should().Be("EUR");
        payment.ChannelReference.Should().Be("ref_action_1");
        payment.ThreeDsAction.Should().BeEquivalentTo(actionDetails);
        payment.Version.Should().Be(1);

        payment.DomainEvents.Should().ContainSingle();
        var evt = payment.DomainEvents.First() as PaymentTransitionDomainEvent;
        evt.Should().NotBeNull();
        evt!.Outcome.Should().Be(PaymentLifecycleOutcome.RequiresAction);
        evt.Amount.Should().Be(10000);
        evt.Currency.Should().Be("EUR");
    }

    [Fact]
    public void CompleteThreeDs_WhenApproved_TransitionsToAuthorized_AndEmitsDomainEvent()
    {
        var actionDetails = new ThreeDsActionDetails("threeDS2", "https://bank.example.com/challenge", "token_challenge");
        var payment = Payment.CreateRequiresAction("merch_acme", 5000, "EUR", "mocked-adyen", "ref_orig", actionDetails);
        payment.ClearDomainEvents();

        payment.CompleteThreeDs(approved: true, channelReference: "ref_auth_3ds");

        payment.State.Should().Be(PaymentState.Authorized);
        payment.ChannelReference.Should().Be("ref_auth_3ds");
        payment.ThreeDsAction.Should().BeNull();
        payment.Version.Should().Be(2);

        payment.DomainEvents.Should().ContainSingle();
        var evt = payment.DomainEvents.First() as PaymentTransitionDomainEvent;
        evt.Should().NotBeNull();
        evt!.Outcome.Should().Be(PaymentLifecycleOutcome.Authorized);
        evt.Version.Should().Be(2);
    }

    [Fact]
    public void CompleteThreeDs_WhenFailed_TransitionsToDeclined_WithReason()
    {
        var actionDetails = new ThreeDsActionDetails("redirect", "https://bank.example.com/3ds", "token_challenge");
        var payment = Payment.CreateRequiresAction("merch_acme", 5000, "EUR", "mocked-adyen", "ref_orig", actionDetails);
        payment.ClearDomainEvents();

        payment.CompleteThreeDs(approved: false, channelReference: "ref_declined_3ds", declineReason: "ThreeDsFailed");

        payment.State.Should().Be(PaymentState.Declined);
        payment.DeclineReason.Should().Be("ThreeDsFailed");
        payment.ThreeDsAction.Should().BeNull();
        payment.Version.Should().Be(2);

        payment.DomainEvents.Should().ContainSingle();
        var evt = payment.DomainEvents.First() as PaymentTransitionDomainEvent;
        evt.Should().NotBeNull();
        evt!.Outcome.Should().Be(PaymentLifecycleOutcome.Declined);
    }

    [Fact]
    public void CompleteThreeDs_WhenChallengeExpired_TransitionsToDeclined_WithExpiredReason()
    {
        var expiredTime = DateTimeOffset.UtcNow.AddMinutes(-5);
        var actionDetails = new ThreeDsActionDetails("redirect", "https://bank.example.com/3ds", "token_challenge", expiredTime);
        var payment = Payment.CreateRequiresAction("merch_acme", 5000, "EUR", "mocked-adyen", "ref_orig", actionDetails);
        payment.ClearDomainEvents();

        payment.CompleteThreeDs(approved: true, channelReference: "ref_attempt");

        payment.State.Should().Be(PaymentState.Declined);
        payment.DeclineReason.Should().Be("ThreeDsExpired");
        payment.Version.Should().Be(2);

        var evt = payment.DomainEvents.First() as PaymentTransitionDomainEvent;
        evt.Should().NotBeNull();
        evt!.Outcome.Should().Be(PaymentLifecycleOutcome.Declined);
    }

    [Theory]
    [InlineData(PaymentState.Authorized)]
    [InlineData(PaymentState.Declined)]
    [InlineData(PaymentState.Captured)]
    [InlineData(PaymentState.Cancelled)]
    [InlineData(PaymentState.Pending)]
    [InlineData(PaymentState.Unknown)]
    public void CompleteThreeDs_WhenNotInRequiresAction_ThrowsPaymentInvalidStateException(PaymentState invalidInitialState)
    {
        var payment = new Payment("pay_1", "merch_acme", 1000, "EUR", "mocked-adyen", invalidInitialState);

        var act = () => payment.CompleteThreeDs(approved: true, channelReference: "ref_1");

        act.Should().Throw<PaymentInvalidStateException>()
            .Which.RequestedTransition.Should().Be("CompleteThreeDs");
    }

    [Fact]
    public void ApplyAcquirerOutcome_FromRequiresAction_AllowsAuthorizedAndDeclined()
    {
        var payment = Payment.CreateRequiresAction(
            "merch_acme",
            5000,
            "EUR",
            "mocked-adyen",
            "ref_action",
            new ThreeDsActionDetails("redirect", "https://bank.example.com", "token"));

        var applied = payment.ApplyAcquirerOutcome(PaymentLifecycleOutcome.Authorized, "ref_final");
        applied.Should().BeTrue();
        payment.State.Should().Be(PaymentState.Authorized);
        payment.ChannelReference.Should().Be("ref_final");
    }

    [Fact]
    public void ExpireThreeDs_TransitionsToDeclined_WithThreeDsExpiredReason()
    {
        var actionDetails = new ThreeDsActionDetails("threeDS2", "https://bank.example.com", "token");
        var payment = Payment.CreateRequiresAction("merch_acme", 5000, "EUR", "mocked-adyen", "ref_orig", actionDetails);
        payment.ClearDomainEvents();

        payment.ExpireThreeDs();

        payment.State.Should().Be(PaymentState.Declined);
        payment.DeclineReason.Should().Be("ThreeDsExpired");
        payment.ThreeDsAction.Should().BeNull();
        payment.Version.Should().Be(2);

        payment.DomainEvents.Should().ContainSingle();
        var evt = payment.DomainEvents.First() as PaymentTransitionDomainEvent;
        evt.Should().NotBeNull();
        evt!.Outcome.Should().Be(PaymentLifecycleOutcome.Declined);
    }

    [Fact]
    public void ExpireThreeDs_WhenNotInRequiresAction_ThrowsPaymentInvalidStateException()
    {
        var payment = new Payment("pay_1", "merch_acme", 1000, "EUR", "mocked-adyen", PaymentState.Authorized);

        var act = () => payment.ExpireThreeDs();

        act.Should().Throw<PaymentInvalidStateException>()
            .Which.RequestedTransition.Should().Be("ExpireThreeDs");
    }
}
