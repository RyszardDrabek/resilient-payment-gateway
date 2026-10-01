using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PaymentGateway.Infrastructure.Adyen;
using Xunit;

namespace PaymentGateway.IntegrationTests.Adyen;

public sealed class AdyenSettlementIntegrationTests : IDisposable
{
    private readonly AdyenWireMockServer _mockServer;
    private readonly HttpClient _httpClient;
    private readonly AdyenSettlementPort _port;

    public AdyenSettlementIntegrationTests()
    {
        var options = Options.Create(new AdyenOptions
        {
            AutoStartMockServer = true,
            MockServerPort = 0,
            TimeoutSeconds = 2
        });

        _mockServer = new AdyenWireMockServer(options, NullLogger<AdyenWireMockServer>.Instance);
        _mockServer.Start();

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(2)
        };

        _port = new AdyenSettlementPort(_httpClient, options, _mockServer, NullLogger<AdyenSettlementPort>.Instance);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _mockServer.Dispose();
    }

    [Fact]
    public async Task Settle_Authorize_Through_WireMock_Supplies_Merchant_And_Acquirer_References()
    {
        // Act (AC-1, AC-2, AC-4)
        var result = await _port.AuthorizeAsync("party_wiremock_integration", 4200, "EUR");

        // Assert
        result.IsAuthorized.Should().BeTrue();
        result.IsUnanswered.Should().BeFalse();
        result.Channel.Should().Be("ADYEN");
        result.ChannelReference.Should().StartWith("adyen_auth_");
        result.MerchantReference.Should().StartWith("mref_");
    }

    [Fact]
    public async Task Settle_Capture_Refund_Cancel_Through_WireMock_Supplies_Distinct_References()
    {
        // Arrange & Act (AC-3)
        var authResult = await _port.AuthorizeAsync("party_full_lifecycle", 5000, "EUR");
        authResult.IsAuthorized.Should().BeTrue();

        var capResult = await _port.CaptureAsync("pay_lifecycle_test", authResult.ChannelReference, 5000, "EUR");
        var refResult = await _port.RefundAsync("pay_lifecycle_test", authResult.ChannelReference, 2000, "EUR");
        var cncResult = await _port.CancelAsync("pay_lifecycle_test", authResult.ChannelReference);

        // Assert
        capResult.IsAuthorized.Should().BeTrue();
        refResult.IsAuthorized.Should().BeTrue();
        cncResult.IsAuthorized.Should().BeTrue();

        capResult.ChannelReference.Should().StartWith("adyen_cap_");
        refResult.ChannelReference.Should().StartWith("adyen_ref_");
        cncResult.ChannelReference.Should().StartWith("adyen_cnc_");

        new[] { authResult.ChannelReference, capResult.ChannelReference, refResult.ChannelReference, cncResult.ChannelReference }
            .Distinct()
            .Should()
            .HaveCount(4);
    }

    [Fact]
    public async Task Settle_Unanswered_Outcome_When_Mock_Unreachable_Or_Exceeds_Deadline()
    {
        // Arrange (AC-5) - pass "timeout" in partyId to trigger mock server delay beyond client deadline
        var result = await _port.AuthorizeAsync("timeout_party", 9900, "EUR");

        // Assert
        result.IsAuthorized.Should().BeFalse();
        result.IsUnanswered.Should().BeTrue();
        result.Channel.Should().Be("ADYEN");
        result.ChannelReference.Should().BeEmpty();
        result.DeclineReason.Should().NotBeNullOrWhiteSpace();
    }
}
