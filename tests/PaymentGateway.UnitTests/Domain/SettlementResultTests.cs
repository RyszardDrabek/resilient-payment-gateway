using FluentAssertions;
using PaymentGateway.Domain.Ports;
using Xunit;

namespace PaymentGateway.UnitTests.Domain;

public class SettlementResultTests
{
    [Fact]
    public void Success_CreatesAuthorizedSuccessfulResult()
    {
        // Act
        var result = SettlementResult.Success("ADYEN", "psp_auth_123", "mref_999");

        // Assert
        result.IsAuthorized.Should().BeTrue();
        result.IsUnanswered.Should().BeFalse();
        result.IsSuccessful.Should().BeTrue();
        result.Channel.Should().Be("ADYEN");
        result.ChannelReference.Should().Be("psp_auth_123");
        result.MerchantReference.Should().Be("mref_999");
        result.DeclineReason.Should().BeNull();
    }

    [Fact]
    public void Declined_CreatesRefusedResult()
    {
        // Act
        var result = SettlementResult.Declined("ADYEN", "psp_ref_456", "Refused", "mref_888");

        // Assert
        result.IsAuthorized.Should().BeFalse();
        result.IsUnanswered.Should().BeFalse();
        result.IsSuccessful.Should().BeFalse();
        result.Channel.Should().Be("ADYEN");
        result.ChannelReference.Should().Be("psp_ref_456");
        result.MerchantReference.Should().Be("mref_888");
        result.DeclineReason.Should().Be("Refused");
    }

    [Fact]
    public void Unanswered_CreatesUnansweredNonSuccessResult()
    {
        // Act
        var result = SettlementResult.Unanswered("ADYEN", "Connection timed out", "mref_777");

        // Assert
        result.IsAuthorized.Should().BeFalse();
        result.IsUnanswered.Should().BeTrue();
        result.IsSuccessful.Should().BeFalse();
        result.Channel.Should().Be("ADYEN");
        result.ChannelReference.Should().BeEmpty();
        result.MerchantReference.Should().Be("mref_777");
        result.DeclineReason.Should().Be("Connection timed out");
    }

    [Fact]
    public void Constructor_SupportsLegacyParameters()
    {
        // Act
        var result = new SettlementResult(true, "MOCK", "ref_001");

        // Assert
        result.IsAuthorized.Should().BeTrue();
        result.IsUnanswered.Should().BeFalse();
        result.IsSuccessful.Should().BeTrue();
        result.Channel.Should().Be("MOCK");
        result.ChannelReference.Should().Be("ref_001");
        result.DeclineReason.Should().BeNull();
        result.MerchantReference.Should().BeNull();
    }
}
