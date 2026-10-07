using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PaymentGateway.Infrastructure.Adyen;
using PaymentGateway.Infrastructure.Adyen.Models;
using Xunit;

namespace PaymentGateway.UnitTests.Infrastructure;

public class AdyenLiveSandboxModeTests
{
    private class TestHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handlerFunc) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }
        public int CallCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            if (request.Content is not null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }
            return await handlerFunc(request);
        }
    }

    [Fact]
    public async Task AuthorizeAsync_LiveModeWithValidCredentials_CallsLiveSandboxWithApiKeyHeader()
    {
        // Arrange (AC-1, AC-4)
        var expectedPsp = "live_psp_auth_7777";
        var expectedApiKey = "AQEyhmfxLI2Pb...testkey";
        var expectedMerchant = "InterparkingSandbox";

        var handler = new TestHttpMessageHandler(req =>
        {
            var response = new AdyenPaymentResponse(expectedPsp, "Authorised", null, "mref_live_1");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(response), System.Text.Encoding.UTF8, "application/json")
            });
        });

        var client = new HttpClient(handler);
        var options = Options.Create(new AdyenOptions
        {
            Mode = "Live",
            ApiKey = expectedApiKey,
            MerchantAccount = expectedMerchant
        });

        var port = new AdyenSettlementPort(client, options, mockServer: null, NullLogger<AdyenSettlementPort>.Instance);

        // Act
        var result = await port.AuthorizeAsync("party_live_test", 10000, "EUR");

        // Assert
        result.IsAuthorized.Should().BeTrue();
        result.IsSuccessful.Should().BeTrue();
        result.IsConfigurationFail.Should().BeFalse();
        result.Channel.Should().Be("ADYEN");
        result.ChannelReference.Should().Be(expectedPsp);
        result.MerchantReference.Should().StartWith("mref_");

        // Verify HTTP request destination and headers
        handler.CallCount.Should().Be(1);
        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.RequestUri!.ToString().Should().StartWith(AdyenEndpoints.LiveSandboxBaseUrl);
        handler.LastRequest.Headers.GetValues("X-API-Key").Should().ContainSingle().Which.Should().Be(expectedApiKey);
        handler.LastRequestBody.Should().Contain(expectedMerchant);
    }

    [Fact]
    public async Task Modifications_LiveModeWithValidCredentials_CallsLiveSandboxWithApiKeyHeader()
    {
        // Arrange (AC-1, AC-4)
        var capPsp = "live_psp_cap_111";
        var refPsp = "live_psp_ref_222";
        var cncPsp = "live_psp_cnc_333";
        var apiKey = "test_api_key_valid";

        var handler = new TestHttpMessageHandler(req =>
        {
            var path = req.RequestUri?.AbsolutePath ?? string.Empty;
            string psp = path switch
            {
                var p when p.EndsWith("/captures") => capPsp,
                var p when p.EndsWith("/refunds") => refPsp,
                _ => cncPsp
            };

            var response = new AdyenModificationResponse(psp, "live_auth_orig", "received", "mod_ref");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(response), System.Text.Encoding.UTF8, "application/json")
            });
        });

        var client = new HttpClient(handler);
        var options = Options.Create(new AdyenOptions
        {
            Mode = "Live",
            ApiKey = apiKey,
            MerchantAccount = "InterparkingSandbox"
        });

        var port = new AdyenSettlementPort(client, options, mockServer: null, NullLogger<AdyenSettlementPort>.Instance);

        // Act
        var capResult = await port.CaptureAsync("pay_1", "live_auth_orig", 5000, "EUR");
        var refResult = await port.RefundAsync("pay_1", "live_auth_orig", 2000, "EUR");
        var cncResult = await port.CancelAsync("pay_1", "live_auth_orig");

        // Assert
        capResult.IsAuthorized.Should().BeTrue();
        capResult.ChannelReference.Should().Be(capPsp);

        refResult.IsAuthorized.Should().BeTrue();
        refResult.ChannelReference.Should().Be(refPsp);

        cncResult.IsAuthorized.Should().BeTrue();
        cncResult.ChannelReference.Should().Be(cncPsp);

        new[] { capResult.ChannelReference, refResult.ChannelReference, cncResult.ChannelReference }
            .Distinct()
            .Should()
            .HaveCount(3);

        handler.LastRequest!.Headers.GetValues("X-API-Key").Should().Contain(apiKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AuthorizeAsync_LiveModeWithAbsentApiKey_RefusesSettlementWithConfigurationFail(string? absentApiKey)
    {
        // Arrange (AC-2)
        var handler = new TestHttpMessageHandler(req =>
            throw new InvalidOperationException("Network must not be reached when credentials are absent."));

        var client = new HttpClient(handler);
        var options = Options.Create(new AdyenOptions
        {
            Mode = "Live",
            ApiKey = absentApiKey,
            MerchantAccount = "ValidMerchantAccount"
        });

        var port = new AdyenSettlementPort(client, options, mockServer: null, NullLogger<AdyenSettlementPort>.Instance);

        // Act
        var result = await port.AuthorizeAsync("party_test", 5000, "EUR");

        // Assert
        result.IsAuthorized.Should().BeFalse();
        result.IsSuccessful.Should().BeFalse();
        result.IsConfigurationFail.Should().BeTrue();
        result.ChannelReference.Should().BeEmpty();
        result.DeclineReason.Should().Contain("credentials are absent or invalid");
        handler.CallCount.Should().Be(0);
    }

    [Theory]
    [InlineData("INVALID")]
    [InlineData("REPLACE_ME")]
    public async Task AuthorizeAsync_LiveModeWithInvalidApiKey_RefusesSettlementWithConfigurationFail(string invalidApiKey)
    {
        // Arrange (AC-2)
        var handler = new TestHttpMessageHandler(req =>
            throw new InvalidOperationException("Network must not be reached when credentials are invalid placeholder."));

        var client = new HttpClient(handler);
        var options = Options.Create(new AdyenOptions
        {
            Mode = "Live",
            ApiKey = invalidApiKey,
            MerchantAccount = "ValidMerchantAccount"
        });

        var port = new AdyenSettlementPort(client, options, mockServer: null, NullLogger<AdyenSettlementPort>.Instance);

        // Act
        var result = await port.AuthorizeAsync("party_test", 5000, "EUR");

        // Assert
        result.IsAuthorized.Should().BeFalse();
        result.IsSuccessful.Should().BeFalse();
        result.IsConfigurationFail.Should().BeTrue();
        result.DeclineReason.Should().Contain("credentials are absent or invalid");
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Modifications_LiveModeWithAbsentApiKey_RefusesSettlementWithConfigurationFail()
    {
        // Arrange (AC-2)
        var handler = new TestHttpMessageHandler(req =>
            throw new InvalidOperationException("Network must not be reached when credentials are absent."));

        var client = new HttpClient(handler);
        var options = Options.Create(new AdyenOptions
        {
            Mode = "Live",
            ApiKey = null,
            MerchantAccount = "ValidMerchantAccount"
        });

        var port = new AdyenSettlementPort(client, options, mockServer: null, NullLogger<AdyenSettlementPort>.Instance);

        // Act
        var capResult = await port.CaptureAsync("pay_1", "auth_ref", 5000, "EUR");
        var refResult = await port.RefundAsync("pay_1", "auth_ref", 2000, "EUR");
        var cncResult = await port.CancelAsync("pay_1", "auth_ref");

        // Assert
        capResult.IsConfigurationFail.Should().BeTrue();
        refResult.IsConfigurationFail.Should().BeTrue();
        cncResult.IsConfigurationFail.Should().BeTrue();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task AuthorizeAsync_WhenLiveSandboxReturns401Unauthorized_ReturnsConfigurationFail()
    {
        // Arrange (AC-2)
        var handler = new TestHttpMessageHandler(req =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        var client = new HttpClient(handler);
        var options = Options.Create(new AdyenOptions
        {
            Mode = "Live",
            ApiKey = "bad_api_key",
            MerchantAccount = "ValidMerchantAccount"
        });

        var port = new AdyenSettlementPort(client, options, mockServer: null, NullLogger<AdyenSettlementPort>.Instance);

        // Act
        var result = await port.AuthorizeAsync("party_test", 5000, "EUR");

        // Assert
        result.IsAuthorized.Should().BeFalse();
        result.IsSuccessful.Should().BeFalse();
        result.IsConfigurationFail.Should().BeTrue();
        result.DeclineReason.Should().Contain("rejected by sandbox: 401");
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task AuthorizeAsync_WhenLiveSandboxReturns403Forbidden_ReturnsConfigurationFail()
    {
        // Arrange (AC-2)
        var handler = new TestHttpMessageHandler(req =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)));

        var client = new HttpClient(handler);
        var options = Options.Create(new AdyenOptions
        {
            Mode = "Live",
            ApiKey = "forbidden_api_key",
            MerchantAccount = "ValidMerchantAccount"
        });

        var port = new AdyenSettlementPort(client, options, mockServer: null, NullLogger<AdyenSettlementPort>.Instance);

        // Act
        var result = await port.AuthorizeAsync("party_test", 5000, "EUR");

        // Assert
        result.IsAuthorized.Should().BeFalse();
        result.IsSuccessful.Should().BeFalse();
        result.IsConfigurationFail.Should().BeTrue();
        result.DeclineReason.Should().Contain("rejected by sandbox: 403");
        handler.CallCount.Should().Be(1);
    }
}
