using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PaymentGateway.Infrastructure.Fx;
using PaymentGateway.Infrastructure.Options;
using Xunit;

namespace PaymentGateway.UnitTests.Infrastructure;

public sealed class NbpFxFeedClientTests
{
    [Fact]
    public async Task FetchRatesToUsdAsync_ParsesAndNormalizesRatesAgainstUsd()
    {
        var sampleJson = """
        [
          {
            "table": "A",
            "no": "205/A/NBP/2026",
            "effectiveDate": "2026-10-21",
            "rates": [
              { "currency": "dolar amerykański", "code": "USD", "mid": 4.0000 },
              { "currency": "euro", "code": "EUR", "mid": 4.4000 },
              { "currency": "funt szterling", "code": "GBP", "mid": 5.2000 }
            ]
          }
        ]
        """;

        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, sampleJson);
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new FxRateRefreshOptions());
        var client = new NbpFxFeedClient(httpClient, options, NullLogger<NbpFxFeedClient>.Instance);

        var rates = await client.FetchRatesToUsdAsync(CancellationToken.None);

        rates.Should().NotBeNull();
        rates!["USD"].Should().Be(1.0000m);
        // 1 EUR = 4.40 PLN / 4.00 PLN = 1.1000 USD
        rates["EUR"].Should().Be(1.1000m);
        // 1 GBP = 5.20 PLN / 4.00 PLN = 1.3000 USD
        rates["GBP"].Should().Be(1.3000m);
        // 1 PLN = 1 / 4.00 = 0.2500 USD
        rates["PLN"].Should().Be(0.2500m);
    }

    [Fact]
    public async Task FetchRatesToUsdAsync_WhenHttpError_ReturnsNullGracefully()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.InternalServerError, "Error");
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new FxRateRefreshOptions());
        var client = new NbpFxFeedClient(httpClient, options, NullLogger<NbpFxFeedClient>.Instance);

        var rates = await client.FetchRatesToUsdAsync(CancellationToken.None);

        rates.Should().BeNull();
    }

    private sealed class FakeHttpMessageHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
