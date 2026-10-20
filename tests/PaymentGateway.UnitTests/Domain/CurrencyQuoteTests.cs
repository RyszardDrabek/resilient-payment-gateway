using FluentAssertions;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using Xunit;

namespace PaymentGateway.UnitTests.Domain;

public sealed class CurrencyQuoteTests
{
    [Fact]
    public void Create_AppliesSpreadAndCalculatesTargetAmount()
    {
        var now = DateTimeOffset.UtcNow;
        // Base: PLN, Target: USDC. 1 PLN = 0.2520 USDC spot.
        // Base amount: 10000 (100.00 PLN).
        // Spread: 50 bps (0.50%). Multiplier = 0.9950.
        // Effective rate = 0.2520 * 0.995 = 0.25074.
        // Target amount = 10000 * 0.25074 = 2507 (25.07 USDC).
        var quote = CurrencyQuote.Create(
            id: "fxq_test_1",
            partyId: "merchant_1",
            baseCurrency: "PLN",
            baseAmount: 10000,
            targetCurrency: "USDC",
            spotRate: 0.2520m,
            spreadBps: 50,
            ttl: TimeSpan.FromMinutes(15),
            now: now);

        quote.Id.Should().Be("fxq_test_1");
        quote.PartyId.Should().Be("merchant_1");
        quote.BaseCurrency.Should().Be("PLN");
        quote.BaseAmount.Should().Be(10000);
        quote.TargetCurrency.Should().Be("USDC");
        quote.SpotRate.Should().Be(0.2520m);
        quote.SpreadBps.Should().Be(50);
        quote.ExchangeRate.Should().Be(0.25074m);
        quote.TargetAmount.Should().Be(2507);
        quote.Status.Should().Be(CurrencyQuoteStatus.Active);
        quote.CreatedAt.Should().Be(now);
        quote.ExpiresAt.Should().Be(now.AddMinutes(15));
        quote.IsValid(now).Should().BeTrue();
    }

    [Fact]
    public void IsValid_ReturnsFalse_WhenExpired()
    {
        var now = DateTimeOffset.UtcNow;
        var quote = CurrencyQuote.Create(
            "fxq_expired", null, "EUR", 5000, "USDC", 1.0850m, 50, TimeSpan.FromMinutes(15), now);

        quote.IsValid(now.AddMinutes(14)).Should().BeTrue();
        quote.IsValid(now.AddMinutes(15)).Should().BeFalse();
        quote.IsValid(now.AddMinutes(16)).Should().BeFalse();
    }

    [Fact]
    public void Consume_MarksQuoteConsumed_AndIsIdempotentForSamePaymentId()
    {
        var now = DateTimeOffset.UtcNow;
        var quote = CurrencyQuote.Create(
            "fxq_consume", "m1", "EUR", 5000, "USDC", 1.0850m, 50, TimeSpan.FromMinutes(15), now);

        quote.Consume("pay_123", now.AddMinutes(2));

        quote.Status.Should().Be(CurrencyQuoteStatus.Consumed);
        quote.ConsumedByPaymentId.Should().Be("pay_123");
        quote.ConsumedAt.Should().Be(now.AddMinutes(2));

        // Idempotent re-consumption
        var actIdempotent = () => quote.Consume("pay_123", now.AddMinutes(3));
        actIdempotent.Should().NotThrow();

        // Conflict consumption by another payment
        var actConflict = () => quote.Consume("pay_456", now.AddMinutes(4));
        actConflict.Should().Throw<InvalidOperationException>()
            .WithMessage("*not active*");
    }

    [Fact]
    public void Consume_Throws_WhenExpired()
    {
        var now = DateTimeOffset.UtcNow;
        var quote = CurrencyQuote.Create(
            "fxq_exp_consume", "m1", "EUR", 5000, "USDC", 1.0850m, 50, TimeSpan.FromMinutes(15), now);

        var act = () => quote.Consume("pay_123", now.AddMinutes(16));
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*expired*");

        quote.Status.Should().Be(CurrencyQuoteStatus.Expired);
    }

    [Fact]
    public void Expire_TransitionsStatusToExpired()
    {
        var now = DateTimeOffset.UtcNow;
        var quote = CurrencyQuote.Create(
            "fxq_exp", null, "EUR", 5000, "USDC", 1.0850m, 50, TimeSpan.FromMinutes(15), now);

        quote.Expire(now.AddMinutes(10));
        quote.Status.Should().Be(CurrencyQuoteStatus.Active);

        quote.Expire(now.AddMinutes(16));
        quote.Status.Should().Be(CurrencyQuoteStatus.Expired);
    }
}
