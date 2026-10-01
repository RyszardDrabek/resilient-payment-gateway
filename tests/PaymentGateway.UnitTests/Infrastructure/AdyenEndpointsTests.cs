using FluentAssertions;
using PaymentGateway.Infrastructure.Adyen;
using Xunit;

namespace PaymentGateway.UnitTests.Infrastructure;

public class AdyenEndpointsTests
{
    [Fact]
    public void Endpoints_ReturnExpectedPaths_WithDefaultVersion()
    {
        AdyenEndpoints.Payments().Should().Be("/v71/payments");
        AdyenEndpoints.Captures("psp_123").Should().Be("/v71/payments/psp_123/captures");
        AdyenEndpoints.Refunds("psp_123").Should().Be("/v71/payments/psp_123/refunds");
        AdyenEndpoints.Cancels("psp_123").Should().Be("/v71/payments/psp_123/cancels");
        AdyenEndpoints.PaymentsPathPrefix().Should().Be("/v71/payments/");
    }

    [Fact]
    public void Endpoints_ReturnExpectedPaths_WithCustomVersion()
    {
        const string customVersion = "v72";

        AdyenEndpoints.Payments(customVersion).Should().Be("/v72/payments");
        AdyenEndpoints.Captures("psp_456", customVersion).Should().Be("/v72/payments/psp_456/captures");
        AdyenEndpoints.Refunds("psp_456", customVersion).Should().Be("/v72/payments/psp_456/refunds");
        AdyenEndpoints.Cancels("psp_456", customVersion).Should().Be("/v72/payments/psp_456/cancels");
        AdyenEndpoints.PaymentsPathPrefix(customVersion).Should().Be("/v72/payments/");
    }
}
