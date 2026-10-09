using FluentAssertions;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Events;

namespace PaymentGateway.UnitTests.Domain;

public sealed class PaymentApplyAcquirerOutcomeTests
{
    [Fact]
    public void AC1_WhenDuplicateOutcomeArrives_DoesNotDoubleApplyTransition_AndReturnsFalse()
    {
        // Arrange: payment already authorized
        var payment = Payment.Authorize("cust_1", 1000L, "EUR", "ADYEN", "psp_1");
        payment.ClearDomainEvents();
        var initialVersion = payment.Version;

        // Act: duplicate Authorized arrives
        var applied = payment.ApplyAcquirerOutcome(PaymentLifecycleOutcome.Authorized, "psp_1");

        // Assert
        applied.Should().BeFalse();
        payment.State.Should().Be(PaymentState.Authorized);
        payment.Version.Should().Be(initialVersion);
        payment.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void AC1_WhenOutOfOrderAuthorisationArrivesAfterCapture_DoesNotRevertState_AndReturnsFalse()
    {
        // Arrange: payment is captured
        var payment = Payment.Authorize("cust_1", 1000L, "EUR", "ADYEN", "psp_1");
        payment.Capture("cap_1");
        payment.ClearDomainEvents();
        var capturedVersion = payment.Version;

        // Act: late Authorisation arriving out-of-order
        var applied = payment.ApplyAcquirerOutcome(PaymentLifecycleOutcome.Authorized, "psp_1");

        // Assert: state remains Captured, not reverted
        applied.Should().BeFalse();
        payment.State.Should().Be(PaymentState.Captured);
        payment.Version.Should().Be(capturedVersion);
        payment.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void AC1_WhenOutOfOrderCaptureArrivesAfterRefund_DoesNotRevertState_AndReturnsFalse()
    {
        // Arrange: payment is refunded
        var payment = Payment.Authorize("cust_1", 1000L, "EUR", "ADYEN", "psp_1");
        payment.Capture("cap_1");
        payment.Refund("ref_1");
        payment.ClearDomainEvents();
        var refundedVersion = payment.Version;

        // Act: late Capture or Authorisation notification arrives
        var appliedCapture = payment.ApplyAcquirerOutcome(PaymentLifecycleOutcome.Captured, "cap_1");
        var appliedAuth = payment.ApplyAcquirerOutcome(PaymentLifecycleOutcome.Authorized, "psp_1");

        // Assert
        appliedCapture.Should().BeFalse();
        appliedAuth.Should().BeFalse();
        payment.State.Should().Be(PaymentState.Refunded);
        payment.Version.Should().Be(refundedVersion);
        payment.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void AC2_WhenPendingPaymentResolvesByAcquirerOutcome_TransitionsToOutcome_AndEmitsDomainEvent()
    {
        // Arrange
        var payment = Payment.CreatePending("cust_pending", 4500L, "EUR", "ADYEN", "temp_ref", "channel timeout");
        payment.ClearDomainEvents();
        var initialVersion = payment.Version;

        // Act: verified notification resolves to Authorized
        var applied = payment.ApplyAcquirerOutcome(PaymentLifecycleOutcome.Authorized, "psp_auth_live_999");

        // Assert
        applied.Should().BeTrue();
        payment.State.Should().Be(PaymentState.Authorized);
        payment.ChannelReference.Should().Be("psp_auth_live_999");
        payment.Version.Should().Be(initialVersion + 1);

        payment.DomainEvents.Should().ContainSingle();
        var domainEvt = payment.DomainEvents.Single().Should().BeOfType<PaymentTransitionDomainEvent>().Subject;
        domainEvt.PaymentId.Should().Be(payment.Id);
        domainEvt.Outcome.Should().Be(PaymentLifecycleOutcome.Authorized);
        domainEvt.Version.Should().Be(payment.Version);
    }

    [Fact]
    public void AC2_WhenUnknownPaymentResolvesByAcquirerDecline_TransitionsToDeclined_WithReason()
    {
        // Arrange
        var payment = Payment.CreateUnknown("cust_unknown", 3000L, "EUR", "ADYEN", "unknown_ref", "crash before response");
        payment.ClearDomainEvents();

        // Act: verified notification resolves to Declined
        var applied = payment.ApplyAcquirerOutcome(PaymentLifecycleOutcome.Declined, "psp_declined", "Insufficient funds");

        // Assert
        applied.Should().BeTrue();
        payment.State.Should().Be(PaymentState.Declined);
        payment.DeclineReason.Should().Be("Insufficient funds");
        payment.ChannelReference.Should().Be("psp_declined");

        var domainEvt = payment.DomainEvents.Single().Should().BeOfType<PaymentTransitionDomainEvent>().Subject;
        domainEvt.Outcome.Should().Be(PaymentLifecycleOutcome.Declined);
    }

    [Fact]
    public void AC1_WhenOutOfOrderDeclineArrivesAfterAuthorized_DoesNotRevertState_AndReturnsFalse()
    {
        // Arrange: payment is authorized
        var payment = Payment.Authorize("cust_1", 1000L, "EUR", "ADYEN", "psp_1");
        payment.ClearDomainEvents();
        var authorizedVersion = payment.Version;

        // Act: late/out-of-order Declined outcome arrives
        var applied = payment.ApplyAcquirerOutcome(PaymentLifecycleOutcome.Declined, "psp_declined", "Card expired");

        // Assert: state remains Authorized, not reverted to Declined
        applied.Should().BeFalse();
        payment.State.Should().Be(PaymentState.Authorized);
        payment.Version.Should().Be(authorizedVersion);
        payment.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void FWEB302_AC1_WhenAuthorizedPaymentMovesToPending_TransitionsToPending_AndEmitsDomainEvent()
    {
        // Arrange
        var payment = Payment.Authorize("cust_web3", 5000000L, "USDC", "WEB3", "web3_auth_123");
        payment.ClearDomainEvents();
        var initialVersion = payment.Version;

        // Act: broadcast transfer pending finality
        var applied = payment.ApplyAcquirerOutcome(PaymentLifecycleOutcome.Pending, "0xabcdef1234567890");

        // Assert
        applied.Should().BeTrue();
        payment.State.Should().Be(PaymentState.Pending);
        payment.ChannelReference.Should().Be("0xabcdef1234567890");
        payment.Version.Should().Be(initialVersion + 1);

        var domainEvt = payment.DomainEvents.Single().Should().BeOfType<PaymentTransitionDomainEvent>().Subject;
        domainEvt.Outcome.Should().Be(PaymentLifecycleOutcome.Pending);
    }

    [Fact]
    public void FWEB302_AC1_WhenCapturedPaymentReceivesLatePending_DoesNotRevertState_AndReturnsFalse()
    {
        // Arrange
        var payment = Payment.Authorize("cust_web3", 5000000L, "USDC", "WEB3", "web3_auth_123");
        payment.Capture("0xfinalized_hash");
        payment.ClearDomainEvents();
        var capturedVersion = payment.Version;

        // Act: late pending arrives
        var applied = payment.ApplyAcquirerOutcome(PaymentLifecycleOutcome.Pending, "0xlate_hash");

        // Assert
        applied.Should().BeFalse();
        payment.State.Should().Be(PaymentState.Captured);
        payment.ChannelReference.Should().Be("0xfinalized_hash");
        payment.Version.Should().Be(capturedVersion);
        payment.DomainEvents.Should().BeEmpty();
    }
}
