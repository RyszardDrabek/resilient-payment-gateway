using FluentAssertions;
using PaymentGateway.Domain.Entities;
using Xunit;

namespace PaymentGateway.UnitTests.Domain;

public class PaymentTests
{
    [Fact]
    public void Payment_Authorize_CreatesAuthorizedPaymentWithReference()
    {
        // Act
        var payment = Payment.Authorize("party_123", 1050, "EUR", "MOCK", "ref_mock_001");

        // Assert
        payment.Should().NotBeNull();
        payment.Id.Should().StartWith("pay_");
        payment.PartyId.Should().Be("party_123");
        payment.Amount.Should().Be(1050);
        payment.Currency.Should().Be("EUR");
        payment.SettlementChannel.Should().Be("MOCK");
        payment.State.Should().Be(PaymentState.Authorized);
        payment.ChannelReference.Should().Be("ref_mock_001");
        payment.DeclineReason.Should().BeNull();
    }

    [Fact]
    public void Payment_Decline_CreatesDeclinedPaymentWithReason()
    {
        // Act
        var payment = Payment.Decline("party_123", 2500, "USD", "ADYEN", "ref_declined_002", "Insufficient funds");

        // Assert
        payment.State.Should().Be(PaymentState.Declined);
        payment.ChannelReference.Should().Be("ref_declined_002");
        payment.DeclineReason.Should().Be("Insufficient funds");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void Payment_ThrowsArgumentOutOfRangeException_WhenAmountIsZeroOrNegative(long invalidAmount)
    {
        // Act
        var act = () => Payment.Authorize("party_1", invalidAmount, "EUR", "MOCK", "ref_1");

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("EU")]
    [InlineData("EURO")]
    public void Payment_ThrowsArgumentException_WhenCurrencyIsNotThreeLetters(string invalidCurrency)
    {
        // Act
        var act = () => Payment.Authorize("party_1", 1000, invalidCurrency, "MOCK", "ref_1");

        // Assert
        act.Should().Throw<ArgumentException>();
    }
}
