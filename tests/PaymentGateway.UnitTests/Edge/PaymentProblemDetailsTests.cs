using FluentAssertions;
using PaymentGateway.Edge.Problems;
using Xunit;

namespace PaymentGateway.UnitTests.Edge;

public sealed class PaymentProblemDetailsTests
{
    [Fact]
    public void PaymentProblemDetails_IsImmutable_Record()
    {
        var details = new PaymentProblemDetails(
            Type: PaymentProblemTypes.Validation,
            Title: "Validation Failed",
            Status: 400,
            Detail: "Some detail",
            CorrelationId: "trace-abc-123");

        details.Type.Should().Be(PaymentProblemTypes.Validation);
        details.Title.Should().Be("Validation Failed");
        details.Status.Should().Be(400);
        details.Detail.Should().Be("Some detail");
        details.CorrelationId.Should().Be("trace-abc-123");
    }

    [Fact]
    public void PaymentProblemDetails_SupportsValueEquality()
    {
        var a = new PaymentProblemDetails("urn:rpg:problem:not-found", "Not Found", 404, "Missing", "id-1");
        var b = new PaymentProblemDetails("urn:rpg:problem:not-found", "Not Found", 404, "Missing", "id-1");

        a.Should().Be(b);
    }
}
