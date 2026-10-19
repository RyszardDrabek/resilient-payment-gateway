using FluentAssertions;
using PaymentGateway.Domain.Entities;
using Xunit;

namespace PaymentGateway.UnitTests.Domain;

public sealed class PaymentMethodTokenTests
{
    [Fact]
    public void Constructor_WithValidArguments_InitializesActiveToken()
    {
        // Act
        var token = new PaymentMethodToken(
            "pm_123",
            "party_abc",
            "ADYEN",
            "visa",
            "**** **** **** 1111",
            12,
            2030,
            "adyen_stored_token_999");

        // Assert
        token.Id.Should().Be("pm_123");
        token.PartyId.Should().Be("party_abc");
        token.SettlementChannel.Should().Be("ADYEN");
        token.CardBrand.Should().Be("visa");
        token.MaskedPan.Should().Be("**** **** **** 1111");
        token.ExpiryMonth.Should().Be(12);
        token.ExpiryYear.Should().Be(2030);
        token.Token.Should().Be("adyen_stored_token_999");
        token.Status.Should().Be(PaymentMethodStatus.Active);
        token.RevokedAt.Should().BeNull();
        token.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(-1)]
    public void Constructor_InvalidMonth_ThrowsArgumentOutOfRangeException(int invalidMonth)
    {
        var act = () => new PaymentMethodToken(
            "pm_1", "party_1", "ADYEN", "visa", "**** 1111", invalidMonth, 2030, "tok_1");

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("expiryMonth");
    }

    [Fact]
    public void Constructor_InvalidYear_ThrowsArgumentOutOfRangeException()
    {
        var act = () => new PaymentMethodToken(
            "pm_1", "party_1", "ADYEN", "visa", "**** 1111", 10, 1999, "tok_1");

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("expiryYear");
    }

    [Fact]
    public void Revoke_TransitionsActiveToRevoked()
    {
        // Arrange
        var token = new PaymentMethodToken("pm_1", "party_1", "ADYEN", "visa", "**** 1111", 10, 2030, "tok_1");
        var now = DateTimeOffset.UtcNow;

        // Act
        token.Revoke(now);

        // Assert
        token.Status.Should().Be(PaymentMethodStatus.Revoked);
        token.RevokedAt.Should().Be(now);

        // Revoking again is idempotent
        token.Revoke(now.AddHours(1));
        token.RevokedAt.Should().Be(now);
    }

    [Fact]
    public void IsExpired_CorrectlyEvaluatesDate()
    {
        var token = new PaymentMethodToken("pm_1", "party_1", "ADYEN", "visa", "**** 1111", 5, 2026, "tok_1");

        // Before expiry
        token.IsExpired(new DateTimeOffset(2026, 4, 30, 0, 0, 0, TimeSpan.Zero)).Should().BeFalse();
        token.IsExpired(new DateTimeOffset(2026, 5, 15, 0, 0, 0, TimeSpan.Zero)).Should().BeFalse();

        // After expiry
        token.IsExpired(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)).Should().BeTrue();
        token.IsExpired(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero)).Should().BeTrue();
    }
}
