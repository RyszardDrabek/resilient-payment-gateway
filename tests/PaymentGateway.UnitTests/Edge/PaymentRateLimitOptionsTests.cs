using FluentAssertions;
using PaymentGateway.Edge.Options;
using Xunit;

namespace PaymentGateway.UnitTests.Edge;

public sealed class PaymentRateLimitOptionsTests
{
    [Fact]
    public void PaymentRateLimitOptions_HasExpectedDefaults()
    {
        var options = new PaymentRateLimitOptions();

        options.PermitLimitPerMinute.Should().Be(100);
        options.GlobalConcurrencyLimit.Should().Be(50);
        options.QueueLimit.Should().Be(0);
        PaymentRateLimitOptions.SectionName.Should().Be("RateLimiting");
    }

    [Fact]
    public void PaymentRateLimitOptions_AllowsPropertyMutation()
    {
        var options = new PaymentRateLimitOptions
        {
            PermitLimitPerMinute = 20,
            GlobalConcurrencyLimit = 5,
            QueueLimit = 2
        };

        options.PermitLimitPerMinute.Should().Be(20);
        options.GlobalConcurrencyLimit.Should().Be(5);
        options.QueueLimit.Should().Be(2);
    }
}
