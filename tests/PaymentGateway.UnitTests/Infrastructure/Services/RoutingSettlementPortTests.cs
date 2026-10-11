using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PaymentGateway.Infrastructure.Adyen;
using PaymentGateway.Infrastructure.Services;
using PaymentGateway.Infrastructure.Web3;

namespace PaymentGateway.UnitTests.Infrastructure.Services;

public sealed class RoutingSettlementPortTests
{
    private readonly IServiceProvider _serviceProvider;

    public RoutingSettlementPortTests()
    {
        var services = new ServiceCollection();

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PaymentGateway:MockSettlement:AutoSucceed"] = "true"
        }).Build();

        services.AddSingleton<MockSettlementPort>(new MockSettlementPort(config));

        var web3Options = Options.Create(new Web3Options
        {
            SecretlessMode = true,
            SupportedAssets = ["USDC"]
        });
        var simulatorClient = new LocalWeb3SimulatorClient(web3Options, NullLogger<LocalWeb3SimulatorClient>.Instance);
        services.AddSingleton<Web3SettlementPort>(new Web3SettlementPort(simulatorClient, web3Options, NullLogger<Web3SettlementPort>.Instance));

        var httpClient = new HttpClient(new TestHttpMessageHandler());
        var adyenOptions = Options.Create(new AdyenOptions
        {
            ApiKey = "test_key",
            MerchantAccount = "test_merchant"
        });
        services.AddSingleton<AdyenSettlementPort>(new AdyenSettlementPort(httpClient, adyenOptions));

        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task AuthorizeAsync_WhenChannelIsWeb3_RoutesToWeb3Port()
    {
        // Arrange
        var router = new RoutingSettlementPort(_serviceProvider);

        // Act
        var result = await router.AuthorizeAsync("party_1", 1_000_000, "USDC", channel: "WEB3");

        // Assert
        result.Channel.Should().Be("WEB3");
    }

    [Fact]
    public async Task AuthorizeAsync_WhenChannelIsMock_RoutesToMockPort()
    {
        // Arrange
        var router = new RoutingSettlementPort(_serviceProvider);

        // Act
        var result = await router.AuthorizeAsync("party_1", 100, "EUR", channel: "MOCK");

        // Assert
        result.Channel.Should().Be("MOCK");
    }

    [Fact]
    public async Task AuthorizeAsync_WhenChannelIsNull_FallsBackToConfiguredChannel()
    {
        // Arrange
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PaymentGateway:ActiveChannel"] = "MOCK"
        }).Build();
        var router = new RoutingSettlementPort(_serviceProvider, config);

        // Act
        var result = await router.AuthorizeAsync("party_1", 100, "EUR", channel: null);

        // Assert
        result.Channel.Should().Be("MOCK");
    }

    [Fact]
    public async Task AuthorizeAsync_WhenChannelAndConfigAreDefault_FallsBackToAdyen()
    {
        // Arrange
        var router = new RoutingSettlementPort(_serviceProvider);

        // Act
        var result = await router.AuthorizeAsync("party_1", 100, "EUR", channel: null);

        // Assert
        result.Channel.Should().Be("ADYEN");
    }


    private class TestHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var responseJson = """{"pspReference":"adyen_test_ref","resultCode":"Authorised","merchantReference":"mref"}""";
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
