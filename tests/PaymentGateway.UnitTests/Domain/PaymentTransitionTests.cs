using FluentAssertions;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Events;
using PaymentGateway.Domain.Exceptions;
using Xunit;

namespace PaymentGateway.UnitTests.Domain;

public sealed class PaymentTransitionTests
{
    [Fact]
    public void Capture_WhenAuthorized_TransitionsToCaptured_AndRaisesDomainEvent()
    {
        var payment = Payment.Authorize("party_test", 5000, "EUR", "MOCK", "ref_auth_1");
        payment.ClearDomainEvents();

        payment.Capture("ref_cap_1");

        payment.State.Should().Be(PaymentState.Captured);
        payment.ChannelReference.Should().Be("ref_cap_1");
        payment.Version.Should().Be(2);

        payment.DomainEvents.Should().ContainSingle();
        var evt = payment.DomainEvents.First() as PaymentTransitionDomainEvent;
        evt.Should().NotBeNull();
        evt!.Outcome.Should().Be(PaymentLifecycleOutcome.Captured);
        evt.Amount.Should().Be(5000);
        evt.Currency.Should().Be("EUR");
        evt.Version.Should().Be(2);
    }

    [Theory]
    [InlineData(PaymentState.Declined)]
    [InlineData(PaymentState.Pending)]
    [InlineData(PaymentState.Unknown)]
    [InlineData(PaymentState.Captured)]
    [InlineData(PaymentState.Cancelled)]
    [InlineData(PaymentState.Refunded)]
    public void Capture_WhenNotAuthorized_ThrowsPaymentInvalidStateException(PaymentState initialState)
    {
        var payment = new Payment("pay_1", "party_1", 1000, "EUR", "MOCK", initialState);

        var act = () => payment.Capture("ref_cap_1");

        var ex = act.Should().Throw<PaymentInvalidStateException>().Which;
        ex.CurrentState.Should().Be(initialState);
        ex.RequestedTransition.Should().Be("Capture");
        payment.State.Should().Be(initialState);
    }

    [Fact]
    public void Cancel_WhenAuthorized_TransitionsToCancelled_AndRaisesDomainEvent()
    {
        var payment = Payment.Authorize("party_test", 5000, "EUR", "MOCK", "ref_auth_1");
        payment.ClearDomainEvents();

        payment.Cancel("ref_cnc_1");

        payment.State.Should().Be(PaymentState.Cancelled);
        payment.ChannelReference.Should().Be("ref_cnc_1");
        payment.Version.Should().Be(2);

        payment.DomainEvents.Should().ContainSingle();
        var evt = payment.DomainEvents.First() as PaymentTransitionDomainEvent;
        evt.Should().NotBeNull();
        evt!.Outcome.Should().Be(PaymentLifecycleOutcome.Cancelled);
        evt.Version.Should().Be(2);
    }

    [Theory]
    [InlineData(PaymentState.Declined)]
    [InlineData(PaymentState.Pending)]
    [InlineData(PaymentState.Unknown)]
    [InlineData(PaymentState.Captured)]
    [InlineData(PaymentState.Cancelled)]
    [InlineData(PaymentState.Refunded)]
    public void Cancel_WhenNotAuthorized_ThrowsPaymentInvalidStateException(PaymentState initialState)
    {
        var payment = new Payment("pay_1", "party_1", 1000, "EUR", "MOCK", initialState);

        var act = () => payment.Cancel("ref_cnc_1");

        var ex = act.Should().Throw<PaymentInvalidStateException>().Which;
        ex.CurrentState.Should().Be(initialState);
        ex.RequestedTransition.Should().Be("Cancel");
        payment.State.Should().Be(initialState);
    }

    [Fact]
    public void Refund_WhenCaptured_TransitionsToRefunded_AndRaisesDomainEvent()
    {
        var payment = Payment.Authorize("party_test", 5000, "EUR", "MOCK", "ref_auth_1");
        payment.Capture("ref_cap_1");
        payment.ClearDomainEvents();

        payment.Refund("ref_ref_1");

        payment.State.Should().Be(PaymentState.Refunded);
        payment.ChannelReference.Should().Be("ref_ref_1");
        payment.Version.Should().Be(3);

        payment.DomainEvents.Should().ContainSingle();
        var evt = payment.DomainEvents.First() as PaymentTransitionDomainEvent;
        evt.Should().NotBeNull();
        evt!.Outcome.Should().Be(PaymentLifecycleOutcome.Refunded);
        evt.Version.Should().Be(3);
    }

    [Theory]
    [InlineData(PaymentState.Authorized)]
    [InlineData(PaymentState.Declined)]
    [InlineData(PaymentState.Pending)]
    [InlineData(PaymentState.Unknown)]
    [InlineData(PaymentState.Cancelled)]
    [InlineData(PaymentState.Refunded)]
    public void Refund_WhenNotCaptured_ThrowsPaymentInvalidStateException(PaymentState initialState)
    {
        var payment = new Payment("pay_1", "party_1", 1000, "EUR", "MOCK", initialState);

        var act = () => payment.Refund("ref_ref_1");

        var ex = act.Should().Throw<PaymentInvalidStateException>().Which;
        ex.CurrentState.Should().Be(initialState);
        ex.RequestedTransition.Should().Be("Refund");
        payment.State.Should().Be(initialState);
    }

    [Fact]
    public void Capture_PartialAmount_TransitionsToPartiallyCaptured_UpdatesCapturedAmount_EmitsEvent()
    {
        var payment = Payment.Authorize("party_test", 5000, "EUR", "MOCK", "ref_auth_1");
        payment.ClearDomainEvents();

        payment.Capture("ref_cap_1", 2000);

        payment.State.Should().Be(PaymentState.PartiallyCaptured);
        payment.CapturedAmount.Should().Be(2000);
        payment.RemainingAuthorizedAmount.Should().Be(3000);
        payment.ChannelReference.Should().Be("ref_cap_1");
        payment.Version.Should().Be(2);

        payment.DomainEvents.Should().ContainSingle();
        var evt = payment.DomainEvents.First() as PaymentTransitionDomainEvent;
        evt.Should().NotBeNull();
        evt!.Outcome.Should().Be(PaymentLifecycleOutcome.PartiallyCaptured);
        evt.Amount.Should().Be(2000);
        evt.Currency.Should().Be("EUR");
    }

    [Fact]
    public void Capture_SubsequentPartialAmount_PermittedUpToRemainingAuthorizedAmount()
    {
        var payment = Payment.Authorize("party_test", 5000, "EUR", "MOCK", "ref_auth_1");
        payment.Capture("ref_cap_1", 2000);
        payment.ClearDomainEvents();

        payment.Capture("ref_cap_2", 1500);

        payment.State.Should().Be(PaymentState.PartiallyCaptured);
        payment.CapturedAmount.Should().Be(3500);
        payment.RemainingAuthorizedAmount.Should().Be(1500);
        payment.ChannelReference.Should().Be("ref_cap_2");

        var evt = payment.DomainEvents.First() as PaymentTransitionDomainEvent;
        evt!.Outcome.Should().Be(PaymentLifecycleOutcome.PartiallyCaptured);
        evt.Amount.Should().Be(1500);
    }

    [Fact]
    public void Capture_FinalPartialAmount_ReachingTotal_TransitionsToCaptured()
    {
        var payment = Payment.Authorize("party_test", 5000, "EUR", "MOCK", "ref_auth_1");
        payment.Capture("ref_cap_1", 3000);
        payment.ClearDomainEvents();

        payment.Capture("ref_cap_2", 2000);

        payment.State.Should().Be(PaymentState.Captured);
        payment.CapturedAmount.Should().Be(5000);
        payment.RemainingAuthorizedAmount.Should().Be(0);

        var evt = payment.DomainEvents.First() as PaymentTransitionDomainEvent;
        evt!.Outcome.Should().Be(PaymentLifecycleOutcome.Captured);
        evt.Amount.Should().Be(2000);
    }

    [Fact]
    public void Cancel_WhenPartiallyCaptured_TransitionsToCancelled_PreservingCapturedAmount()
    {
        var payment = Payment.Authorize("party_test", 5000, "EUR", "MOCK", "ref_auth_1");
        payment.Capture("ref_cap_1", 2000);
        payment.ClearDomainEvents();

        payment.Cancel("ref_cnc_1");

        payment.State.Should().Be(PaymentState.Cancelled);
        payment.CapturedAmount.Should().Be(2000);
        payment.RemainingAuthorizedAmount.Should().Be(3000);

        var evt = payment.DomainEvents.First() as PaymentTransitionDomainEvent;
        evt!.Outcome.Should().Be(PaymentLifecycleOutcome.Cancelled);
        evt.Amount.Should().Be(3000);
    }

    [Fact]
    public void Capture_ExceedingRemainingAuthorizedAmount_ThrowsException_WithoutMutatingState()
    {
        var payment = Payment.Authorize("party_test", 5000, "EUR", "MOCK", "ref_auth_1");
        payment.Capture("ref_cap_1", 2000);

        var act = () => payment.Capture("ref_cap_bad", 3001);

        act.Should().Throw<PaymentValidationException>();
        payment.State.Should().Be(PaymentState.PartiallyCaptured);
        payment.CapturedAmount.Should().Be(2000);
        payment.RemainingAuthorizedAmount.Should().Be(3000);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void Capture_NonPositiveAmount_ThrowsException_WithoutMutatingState(long amount)
    {
        var payment = Payment.Authorize("party_test", 5000, "EUR", "MOCK", "ref_auth_1");

        var act = () => payment.Capture("ref_cap_bad", amount);

        act.Should().Throw<PaymentValidationException>();
        payment.State.Should().Be(PaymentState.Authorized);
        payment.CapturedAmount.Should().Be(0);
        payment.RemainingAuthorizedAmount.Should().Be(5000);
    }
}
