using FluentAssertions;
using PaymentGateway.Domain.Entities;
using Xunit;

namespace PaymentGateway.UnitTests.Domain;

public sealed class FeeScheduleTests
{
    [Theory]
    [InlineData(10000, 10, 120, 130)] // €100.00: €0.10 + 1.2% (€1.20) = €1.30 (130 cents)
    [InlineData(5000, 20, 200, 120)]  // €50.00: €0.20 + 2.0% (€1.00) = €1.20 (120 cents)
    [InlineData(0, 10, 120, 0)]       // €0.00: 0 cents
    public void CalculateFee_ComputesFixedPlusBasisPointsAccurately(
        long amount,
        long fixedFee,
        int bps,
        long expectedFee)
    {
        var schedule = new FeeSchedule("fee_1", "merch_acme", "EUR", fixedFee, bps, DateTimeOffset.UtcNow);
        var calculated = schedule.CalculateFee(amount);
        calculated.Should().Be(expectedFee);
    }
}
