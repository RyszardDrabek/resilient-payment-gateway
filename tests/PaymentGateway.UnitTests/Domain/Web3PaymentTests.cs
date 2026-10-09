using FluentAssertions;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;

namespace PaymentGateway.UnitTests.Domain;

public sealed class Web3PaymentTests
{
    [Fact]
    public void Payment_Authorize_WithUsdc_CreatesPaymentWithNativePrecision()
    {
        // AC-1, AC-6: Preserves native 6-decimal minor unit precision (e.g. 1.50 USDC = 1,500,000 minor units)
        const long amount = 1_500_000;
        const string currency = "USDC";

        var payment = Payment.Authorize("party_web3", amount, currency, "WEB3", "0xabc123");

        payment.Should().NotBeNull();
        payment.Currency.Should().Be("USDC");
        payment.Amount.Should().Be(1_500_000);
        payment.SettlementChannel.Should().Be("WEB3");
        payment.State.Should().Be(PaymentState.Authorized);
        payment.ChannelReference.Should().Be("0xabc123");
    }

    [Theory]
    [InlineData("USDC")]
    [InlineData("usdc")]
    [InlineData("USDT")]
    [InlineData("usdt")]
    public void Payment_IsSupportedCryptoAsset_ReturnsTrueForSupportedAssets(string asset)
    {
        Payment.IsSupportedCryptoAsset(asset).Should().BeTrue();
    }

    [Theory]
    [InlineData("BTC_LONG_NAME")]
    [InlineData("UNKNOWN")]
    [InlineData("EURO")]
    public void Payment_IsSupportedCryptoAsset_ReturnsFalseForUnsupportedAssets(string asset)
    {
        Payment.IsSupportedCryptoAsset(asset).Should().BeFalse();
    }

    [Fact]
    public void Payment_Capture_And_Refund_PreserveNativePrecision()
    {
        const long amount = 250_000_000; // 250 USDC with 6 decimal places
        var payment = Payment.Authorize("party_web3", amount, "USDC", "WEB3", "0xauth1");

        payment.Capture("0xcapture_tx_hash");
        payment.State.Should().Be(PaymentState.Captured);
        payment.Amount.Should().Be(250_000_000);
        payment.ChannelReference.Should().Be("0xcapture_tx_hash");

        payment.Refund("0xrefund_tx_hash");
        payment.State.Should().Be(PaymentState.Refunded);
        payment.Amount.Should().Be(250_000_000);
        payment.ChannelReference.Should().Be("0xrefund_tx_hash");
    }
}
