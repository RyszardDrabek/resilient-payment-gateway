using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using PaymentGateway.Infrastructure.Adyen;
using PaymentGateway.Infrastructure.Adyen.Models;
using Xunit;

namespace PaymentGateway.IntegrationTests.Adyen;

public sealed class AdyenWireMockServerTests : IDisposable
{
    private readonly AdyenWireMockServer _mockServer;
    private readonly HttpClient _client;

    public AdyenWireMockServerTests()
    {
        var options = Options.Create(new AdyenOptions
        {
            AutoStartMockServer = true,
            MockServerPort = 0 // dynamic ephemeral port
        });

        _mockServer = new AdyenWireMockServer(options);
        _mockServer.Start();

        _client = new HttpClient
        {
            BaseAddress = new Uri(_mockServer.Url!)
        };
    }

    public void Dispose()
    {
        _client.Dispose();
        _mockServer.Dispose();
    }

    [Fact]
    public async Task WireMock_Authorize_Returns_Authorised_And_Distinct_References()
    {
        // Arrange (AC-1, AC-2, AC-4)
        var paymentRequest = new AdyenPaymentRequest(
            new AdyenAmount("EUR", 3500),
            "InterparkingMockAccount",
            "mref_wiremock_test",
            new AdyenPaymentMethod("scheme", "4111111111111111", "06", "2029", "123", "Test User"));

        // Act
        var response = await _client.PostAsJsonAsync("/v71/payments", paymentRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AdyenPaymentResponse>();
        body.Should().NotBeNull();
        body!.ResultCode.Should().Be("Authorised");
        body.PspReference.Should().StartWith("adyen_auth_");
        body.MerchantReference.Should().Be("mref_wiremock_test");
    }

    [Fact]
    public async Task WireMock_Authorize_DeclineTrigger_Returns_Refused()
    {
        // Arrange
        var paymentRequest = new AdyenPaymentRequest(
            new AdyenAmount("EUR", 1500),
            "InterparkingMockAccount",
            "decline_test_ref",
            new AdyenPaymentMethod("scheme", "4111111111110002", "06", "2029", "123", "Test User"));

        // Act
        var response = await _client.PostAsJsonAsync("/v71/payments", paymentRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AdyenPaymentResponse>();
        body.Should().NotBeNull();
        body!.ResultCode.Should().Be("Refused");
        body.RefusalReason.Should().Be("Refused");
        body.PspReference.Should().StartWith("adyen_auth_");
    }

    [Fact]
    public async Task WireMock_Capture_Refund_Cancel_Return_Distinct_Acquirer_References()
    {
        // Arrange (AC-3)
        var originalPsp = "adyen_auth_orig123";
        var captureReq = new AdyenModificationRequest("InterparkingMockAccount", "cap_ref_1", new AdyenAmount("EUR", 3500));
        var refundReq = new AdyenModificationRequest("InterparkingMockAccount", "ref_ref_1", new AdyenAmount("EUR", 1000));
        var cancelReq = new AdyenModificationRequest("InterparkingMockAccount", "cnc_ref_1");

        // Act
        var capResponse = await _client.PostAsJsonAsync($"/v71/payments/{originalPsp}/captures", captureReq);
        var refResponse = await _client.PostAsJsonAsync($"/v71/payments/{originalPsp}/refunds", refundReq);
        var cncResponse = await _client.PostAsJsonAsync($"/v71/payments/{originalPsp}/cancels", cancelReq);

        // Assert
        capResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        refResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        cncResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var capBody = await capResponse.Content.ReadFromJsonAsync<AdyenModificationResponse>();
        var refBody = await refResponse.Content.ReadFromJsonAsync<AdyenModificationResponse>();
        var cncBody = await cncResponse.Content.ReadFromJsonAsync<AdyenModificationResponse>();

        capBody.Should().NotBeNull();
        refBody.Should().NotBeNull();
        cncBody.Should().NotBeNull();

        capBody!.Status.Should().Be("received");
        refBody!.Status.Should().Be("received");
        cncBody!.Status.Should().Be("received");

        capBody.PspReference.Should().StartWith("adyen_cap_");
        refBody.PspReference.Should().StartWith("adyen_ref_");
        cncBody.PspReference.Should().StartWith("adyen_cnc_");

        capBody.PspReference.Should().NotBe(refBody.PspReference);
        refBody.PspReference.Should().NotBe(cncBody.PspReference);
    }
}
