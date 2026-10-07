using FluentAssertions;
using PaymentGateway.Infrastructure.Adyen;
using Xunit;

namespace PaymentGateway.UnitTests.Infrastructure;

public class AdyenOptionsTests
{
    [Theory]
    [InlineData("Live", true)]
    [InlineData("live", true)]
    [InlineData("Sandbox", true)]
    [InlineData("sandbox", true)]
    [InlineData("LiveSandbox", true)]
    [InlineData("livesandbox", true)]
    [InlineData("WireMock", false)]
    [InlineData("wiremock", false)]
    [InlineData("Mock", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsLiveMode_IdentifiesLiveAndSandboxModes(string? mode, bool expectedIsLive)
    {
        var options = new AdyenOptions { Mode = mode! };
        options.IsLiveMode.Should().Be(expectedIsLive);
    }

    [Theory]
    [InlineData("valid_key_123", "ValidMerchantAccount", true)]
    [InlineData(null, "ValidMerchantAccount", false)]
    [InlineData("", "ValidMerchantAccount", false)]
    [InlineData("   ", "ValidMerchantAccount", false)]
    [InlineData("INVALID", "ValidMerchantAccount", false)]
    [InlineData("REPLACE_ME", "ValidMerchantAccount", false)]
    [InlineData("valid_key_123", null, false)]
    [InlineData("valid_key_123", "", false)]
    [InlineData("valid_key_123", "   ", false)]
    public void HasValidCredentials_ValidatesApiKeyAndMerchantAccount(string? apiKey, string? merchantAccount, bool expectedValid)
    {
        var options = new AdyenOptions
        {
            ApiKey = apiKey,
            MerchantAccount = merchantAccount!
        };

        options.HasValidCredentials.Should().Be(expectedValid);
    }
}
