using FluentAssertions;
using PaymentGateway.Application.Webhooks;
using Xunit;

namespace PaymentGateway.UnitTests.Application;

public sealed class HmacSha256WebhookSignerTests
{
    [Fact]
    public void ComputeSignature_ValidInputs_ReturnsExpectedHexHash()
    {
        var signer = new HmacSha256WebhookSigner();
        var payload = "{\"paymentId\":\"pay_123\",\"amount\":1000}";
        var secret = "test_secret_key_123456789";

        var signature = signer.ComputeSignature(payload, secret);

        signature.Should().NotBeNullOrWhiteSpace();
        signature.Should().MatchRegex("^[a-f0-9]{64}$");

        // Deterministic repeat
        var signature2 = signer.ComputeSignature(payload, secret);
        signature2.Should().Be(signature);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ComputeSignature_InvalidSecret_ThrowsArgumentException(string? invalidSecret)
    {
        var signer = new HmacSha256WebhookSigner();
        var act = () => signer.ComputeSignature("payload", invalidSecret!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ComputeSignature_NullPayload_ThrowsArgumentNullException()
    {
        var signer = new HmacSha256WebhookSigner();
        var act = () => signer.ComputeSignature(null!, "secret");

        act.Should().Throw<ArgumentNullException>();
    }
}
