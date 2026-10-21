using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using PaymentGateway.Infrastructure.Adyen;
using PaymentGateway.Infrastructure.Adyen.Models;
using Xunit;

namespace PaymentGateway.UnitTests.Infrastructure;

public class AdyenSettlementPortTests
{
    private class TestHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handlerFunc) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content is not null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }
            return await handlerFunc(request);
        }
    }

    [Fact]
    public async Task AuthorizeAsync_WhenMockReturnsAuthorised_ReturnsSuccessWithMerchantAndAcquirerReferences()
    {
        // Arrange
        var expectedPsp = "adyen_auth_88888";
        var handler = new TestHttpMessageHandler(req =>
        {
            var response = new AdyenPaymentResponse(expectedPsp, "Authorised", null, "mref_123");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(response), System.Text.Encoding.UTF8, "application/json")
            });
        });

        var client = new HttpClient(handler);
        var options = Options.Create(new AdyenOptions { BaseUrl = "http://mock-adyen.local" });
        var port = new AdyenSettlementPort(client, options);

        // Act
        var result = await port.AuthorizeAsync("party_test", 5000, "EUR");

        // Assert (AC-2)
        result.IsAuthorized.Should().BeTrue();
        result.IsUnanswered.Should().BeFalse();
        result.Channel.Should().Be("ADYEN");
        result.ChannelReference.Should().Be(expectedPsp);
        result.MerchantReference.Should().StartWith("mref_");

        // Assert (AC-4): Outbound HTTP payload carries instrument fields
        handler.LastRequestBody.Should().NotBeNull();
        handler.LastRequestBody.Should().Contain("\"paymentMethod\":");
        handler.LastRequestBody.Should().Contain("\"number\":\"4111111111111111\"");
        handler.LastRequestBody.Should().Contain("\"cvc\":\"737\"");
    }

    [Fact]
    public async Task AuthorizeAsync_WhenMockReturnsRefused_ReturnsDeclinedResult()
    {
        // Arrange
        var handler = new TestHttpMessageHandler(req =>
        {
            var response = new AdyenPaymentResponse("adyen_ref_999", "Refused", "FRAUD", "mref_123");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(response), System.Text.Encoding.UTF8, "application/json")
            });
        });

        var client = new HttpClient(handler);
        var options = Options.Create(new AdyenOptions { BaseUrl = "http://mock-adyen.local" });
        var port = new AdyenSettlementPort(client, options);

        // Act
        var result = await port.AuthorizeAsync("decline_party", 2500, "EUR");

        // Assert
        result.IsAuthorized.Should().BeFalse();
        result.IsUnanswered.Should().BeFalse();
        result.Channel.Should().Be("ADYEN");
        result.ChannelReference.Should().Be("adyen_ref_999");
        result.DeclineReason.Should().Be("FRAUD");
    }

    [Fact]
    public async Task AuthorizeAsync_WhenMockTimesOutOrFails_ReturnsUnansweredWithoutInventingSuccess()
    {
        // Arrange
        var handler = new TestHttpMessageHandler(req =>
        {
            throw new HttpRequestException("Mock connection refused");
        });

        var client = new HttpClient(handler);
        var options = Options.Create(new AdyenOptions { BaseUrl = "http://unreachable-mock.local" });
        var port = new AdyenSettlementPort(client, options);

        // Act
        var result = await port.AuthorizeAsync("party_test", 5000, "EUR");

        // Assert (AC-5)
        result.IsAuthorized.Should().BeFalse();
        result.IsUnanswered.Should().BeTrue();
        result.Channel.Should().Be("ADYEN");
        result.ChannelReference.Should().BeEmpty();
        result.DeclineReason.Should().Contain("Mock connection refused");
    }

    [Fact]
    public async Task AuthorizeAsync_WhenMockReturnsServerError_ReturnsUnanswered()
    {
        // Arrange
        var handler = new TestHttpMessageHandler(req =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        });

        var client = new HttpClient(handler);
        var options = Options.Create(new AdyenOptions { BaseUrl = "http://mock-adyen.local" });
        var port = new AdyenSettlementPort(client, options);

        // Act
        var result = await port.AuthorizeAsync("party_test", 5000, "EUR");

        // Assert (AC-5)
        result.IsAuthorized.Should().BeFalse();
        result.IsUnanswered.Should().BeTrue();
        result.DeclineReason.Should().Contain("500");
    }

    [Fact]
    public async Task Capture_Refund_Cancel_ReturnDistinctAcquirerReferences()
    {
        // Arrange
        var capPsp = "adyen_cap_111";
        var refPsp = "adyen_ref_222";
        var cncPsp = "adyen_cnc_333";

        var handler = new TestHttpMessageHandler(req =>
        {
            var path = req.RequestUri?.AbsolutePath ?? string.Empty;
            string psp = path switch
            {
                var p when p.EndsWith("/captures") => capPsp,
                var p when p.EndsWith("/refunds") => refPsp,
                _ => cncPsp
            };

            var response = new AdyenModificationResponse(psp, "auth_psp_orig", "received", "mod_ref");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(response), System.Text.Encoding.UTF8, "application/json")
            });
        });

        var client = new HttpClient(handler);
        var options = Options.Create(new AdyenOptions { BaseUrl = "http://mock-adyen.local" });
        var port = new AdyenSettlementPort(client, options);

        // Act (AC-3)
        var captureResult = await port.CaptureAsync("pay_1", "auth_psp_orig", 1000, "EUR");
        var refundResult = await port.RefundAsync("pay_1", "auth_psp_orig", 500, "EUR");
        var cancelResult = await port.CancelAsync("pay_1", "auth_psp_orig");

        // Assert
        captureResult.IsAuthorized.Should().BeTrue();
        captureResult.ChannelReference.Should().Be(capPsp);

        refundResult.IsAuthorized.Should().BeTrue();
        refundResult.ChannelReference.Should().Be(refPsp);

        cancelResult.IsAuthorized.Should().BeTrue();
        cancelResult.ChannelReference.Should().Be(cncPsp);

        captureResult.ChannelReference.Should().NotBe(refundResult.ChannelReference);
        refundResult.ChannelReference.Should().NotBe(cancelResult.ChannelReference);
    }

    [Fact]
    public async Task AuthorizeAsync_WhenCircuitBreakerOpens_ShortCircuitsSubsequentRequests()
    {
        var callCount = 0;
        var handler = new TestHttpMessageHandler(req =>
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        });

        var client = new HttpClient(handler);
        var options = Options.Create(new AdyenOptions
        {
            BaseUrl = "http://mock-adyen.local",
            MaxRetryAttempts = 0,
            CircuitBreakerEnabled = true,
            CircuitBreakerMinimumThroughput = 2,
            CircuitBreakerFailureRatio = 0.5,
            CircuitBreakerSamplingDurationSeconds = 10,
            CircuitBreakerBreakDurationSeconds = 10
        });
        var port = new AdyenSettlementPort(client, options);

        // First 2 failures open the circuit
        var res1 = await port.AuthorizeAsync("party_test", 1000, "EUR");
        res1.IsUnanswered.Should().BeTrue();

        var res2 = await port.AuthorizeAsync("party_test", 1000, "EUR");
        res2.IsUnanswered.Should().BeTrue();

        var callsBeforeOpen = callCount;

        // Third call should be short-circuited by the open circuit breaker without invoking the handler
        var res3 = await port.AuthorizeAsync("party_test", 1000, "EUR");
        res3.IsUnanswered.Should().BeTrue();
        res3.DeclineReason.Should().Contain("circuit breaker is OPEN");
        callCount.Should().Be(callsBeforeOpen);
    }

    [Fact]
    public async Task AuthorizeAsync_WithSharedPipeline_CircuitBreakerStatePersistsAcrossDifferentPortInstances()
    {
        var callCount = 0;
        var handler = new TestHttpMessageHandler(req =>
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        });

        var client = new HttpClient(handler);
        var options = new AdyenOptions
        {
            BaseUrl = "http://mock-adyen.local",
            MaxRetryAttempts = 0,
            CircuitBreakerEnabled = true,
            CircuitBreakerMinimumThroughput = 2,
            CircuitBreakerFailureRatio = 0.5,
            CircuitBreakerSamplingDurationSeconds = 10,
            CircuitBreakerBreakDurationSeconds = 10
        };

        // Singleton pipeline shared across distinct port instances (as registered in DI)
        var sharedPipeline = AdyenSettlementPort.CreatePipeline(options);

        var instance1 = new AdyenSettlementPort(client, Options.Create(options), pipeline: sharedPipeline);
        var res1 = await instance1.AuthorizeAsync("party_test", 1000, "EUR");
        res1.IsUnanswered.Should().BeTrue();

        var instance2 = new AdyenSettlementPort(client, Options.Create(options), pipeline: sharedPipeline);
        var res2 = await instance2.AuthorizeAsync("party_test", 1000, "EUR");
        res2.IsUnanswered.Should().BeTrue();

        var callsBeforeOpen = callCount;

        // Distinct instance 3 should see OPEN circuit breaker from the shared singleton pipeline
        var instance3 = new AdyenSettlementPort(client, Options.Create(options), pipeline: sharedPipeline);
        var res3 = await instance3.AuthorizeAsync("party_test", 1000, "EUR");
        res3.IsUnanswered.Should().BeTrue();
        res3.DeclineReason.Should().Contain("circuit breaker is OPEN");
        callCount.Should().Be(callsBeforeOpen);
    }
}
