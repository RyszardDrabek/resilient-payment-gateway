using System.Text.Json;
using FluentAssertions;
using PaymentGateway.Infrastructure.Adyen.Models;
using Xunit;

namespace PaymentGateway.UnitTests.Infrastructure;

public class AdyenWireFormatTests
{
    [Fact]
    public void AdyenPaymentRequest_SerializesToExpectedJsonSchema()
    {
        // Arrange
        var request = new AdyenPaymentRequest(
            new AdyenAmount("EUR", 1250),
            "TestMerchant",
            "mref_12345",
            new AdyenPaymentMethod(
                "scheme",
                "4111111111111111",
                "03",
                "2030",
                "737",
                "J. Doe"));

        // Act
        var json = JsonSerializer.Serialize(request);

        // Assert
        json.Should().Contain("\"currency\":\"EUR\"");
        json.Should().Contain("\"value\":1250");
        json.Should().Contain("\"merchantAccount\":\"TestMerchant\"");
        json.Should().Contain("\"reference\":\"mref_12345\"");
        json.Should().Contain("\"type\":\"scheme\"");
        json.Should().Contain("\"number\":\"4111111111111111\"");
        json.Should().Contain("\"cvc\":\"737\"");
    }

    [Fact]
    public void AdyenPaymentResponse_DeserializesCorrectly()
    {
        // Arrange
        const string json = """
        {
            "pspReference": "8515876543210987",
            "resultCode": "Authorised",
            "merchantReference": "mref_12345"
        }
        """;

        // Act
        var response = JsonSerializer.Deserialize<AdyenPaymentResponse>(json);

        // Assert
        response.Should().NotBeNull();
        response!.PspReference.Should().Be("8515876543210987");
        response.ResultCode.Should().Be("Authorised");
        response.MerchantReference.Should().Be("mref_12345");
        response.RefusalReason.Should().BeNull();
    }

    [Fact]
    public void AdyenModificationResponse_DeserializesCorrectly()
    {
        // Arrange
        const string json = """
        {
            "pspReference": "8815876543210990",
            "paymentPspReference": "8515876543210987",
            "status": "received",
            "reference": "cap_12345"
        }
        """;

        // Act
        var response = JsonSerializer.Deserialize<AdyenModificationResponse>(json);

        // Assert
        response.Should().NotBeNull();
        response!.PspReference.Should().Be("8815876543210990");
        response.PaymentPspReference.Should().Be("8515876543210987");
        response.Status.Should().Be("received");
        response.Reference.Should().Be("cap_12345");
    }
}
