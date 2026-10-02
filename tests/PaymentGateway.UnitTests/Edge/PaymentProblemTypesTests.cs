using FluentAssertions;
using PaymentGateway.Edge.Problems;
using Xunit;

namespace PaymentGateway.UnitTests.Edge;

public sealed class PaymentProblemTypesTests
{
    [Fact]
    public void PaymentProblemTypes_HaveExpectedUrnPrefixes()
    {
        PaymentProblemTypes.Validation.Should().StartWith("urn:rpg:problem:");
        PaymentProblemTypes.NotFound.Should().StartWith("urn:rpg:problem:");
        PaymentProblemTypes.Conflict.Should().StartWith("urn:rpg:problem:");
        PaymentProblemTypes.ServerError.Should().StartWith("urn:rpg:problem:");
        PaymentProblemTypes.RateLimit.Should().StartWith("urn:rpg:problem:");
    }

    [Fact]
    public void PaymentProblemTypes_HaveCorrectSuffixes()
    {
        PaymentProblemTypes.Validation.Should().Be("urn:rpg:problem:validation");
        PaymentProblemTypes.NotFound.Should().Be("urn:rpg:problem:not-found");
        PaymentProblemTypes.Conflict.Should().Be("urn:rpg:problem:conflict");
        PaymentProblemTypes.ServerError.Should().Be("urn:rpg:problem:server-error");
        PaymentProblemTypes.RateLimit.Should().Be("urn:rpg:problem:rate-limit");
    }
}
