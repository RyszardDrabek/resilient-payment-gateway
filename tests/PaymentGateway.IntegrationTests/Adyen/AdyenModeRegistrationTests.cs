using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PaymentGateway.Domain.Ports;
using PaymentGateway.Infrastructure;
using PaymentGateway.Infrastructure.Adyen;
using Xunit;

namespace PaymentGateway.IntegrationTests.Adyen;

public class AdyenModeRegistrationTests
{
    [Fact]
    public void When_Mode_Is_WireMock_MockServer_Is_Registered()
    {
        // Arrange
        var inMemorySettings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:PaymentDb"] = "Host=localhost;Database=test;Username=postgres;Password=postgres",
            ["Adyen:Mode"] = "WireMock",
            ["Adyen:AutoStartMockServer"] = "false"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);

        var serviceProvider = services.BuildServiceProvider();

        // Assert
        serviceProvider.GetService<AdyenWireMockServer>().Should().NotBeNull();
    }

    [Fact]
    public void When_Mode_Is_Live_MockServer_Is_Not_Registered()
    {
        // Arrange
        var inMemorySettings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:PaymentDb"] = "Host=localhost;Database=test;Username=postgres;Password=postgres",
            ["Adyen:Mode"] = "Live",
            ["Adyen:ApiKey"] = "AQEyhmfx...",
            ["Adyen:MerchantAccount"] = "LiveMerchant"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);

        var serviceProvider = services.BuildServiceProvider();

        // Assert (AC-1, AC-3)
        serviceProvider.GetService<AdyenWireMockServer>().Should().BeNull();
    }

    [Fact]
    public async Task When_Mode_Is_Live_And_Credentials_Absent_SettlementPort_Refuses_Settlement()
    {
        // Arrange (AC-2)
        var inMemorySettings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:PaymentDb"] = "Host=localhost;Database=test;Username=postgres;Password=postgres",
            ["Adyen:Mode"] = "Live",
            ["Adyen:ApiKey"] = "",
            ["Adyen:MerchantAccount"] = "LiveMerchant"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);

        var serviceProvider = services.BuildServiceProvider();
        var port = serviceProvider.GetRequiredService<ISettlementPort>();

        // Act
        var result = await port.AuthorizeAsync("test_party", 5000, "EUR");

        // Assert
        result.IsConfigurationFail.Should().BeTrue();
        result.IsSuccessful.Should().BeFalse();
        result.DeclineReason.Should().Contain("credentials are absent or invalid");
    }
}
