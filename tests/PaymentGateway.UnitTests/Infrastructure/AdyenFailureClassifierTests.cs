using System.Net;
using FluentAssertions;
using PaymentGateway.Infrastructure.Adyen;
using Xunit;

namespace PaymentGateway.UnitTests.Infrastructure;

public sealed class AdyenFailureClassifierTests
{
    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData((HttpStatusCode)429)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public void IsRetrySafeStatusCode_WhenTransientOrDegraded_ReturnsTrue(HttpStatusCode statusCode)
    {
        AdyenFailureClassifier.IsRetrySafeStatusCode(statusCode).Should().BeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public void IsRetrySafeStatusCode_WhenTerminalOrSuccess_ReturnsFalse(HttpStatusCode statusCode)
    {
        AdyenFailureClassifier.IsRetrySafeStatusCode(statusCode).Should().BeFalse();
    }

    [Fact]
    public void IsRetrySafeException_WhenNetworkOrTimeout_ReturnsTrue()
    {
        AdyenFailureClassifier.IsRetrySafeException(new HttpRequestException("network down")).Should().BeTrue();
        AdyenFailureClassifier.IsRetrySafeException(new TimeoutException("timeout")).Should().BeTrue();
        AdyenFailureClassifier.IsRetrySafeException(new TaskCanceledException("canceled")).Should().BeTrue();
    }

    [Fact]
    public void IsRetrySafeException_WhenBusinessOrValidationException_ReturnsFalse()
    {
        AdyenFailureClassifier.IsRetrySafeException(new ArgumentException("invalid")).Should().BeFalse();
        AdyenFailureClassifier.IsRetrySafeException(new InvalidOperationException("invalid state")).Should().BeFalse();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public void IsTerminalClientError_WhenClientPayloadInvalid_ReturnsTrue(HttpStatusCode statusCode)
    {
        AdyenFailureClassifier.IsTerminalClientError(statusCode).Should().BeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void IsAuthenticationError_WhenCredentialsRejected_ReturnsTrue(HttpStatusCode statusCode)
    {
        AdyenFailureClassifier.IsAuthenticationError(statusCode).Should().BeTrue();
    }
}
