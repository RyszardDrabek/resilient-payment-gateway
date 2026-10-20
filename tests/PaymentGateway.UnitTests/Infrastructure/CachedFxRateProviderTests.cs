using FluentAssertions;
using PaymentGateway.Infrastructure.Fx;
using Xunit;

namespace PaymentGateway.UnitTests.Infrastructure;

public sealed class CachedFxRateProviderTests
{
    private readonly CachedFxRateProvider _provider = new();

    [Fact]
    public async Task GetSpotRateAsync_SameCurrency_ReturnsOne()
    {
        var rate = await _provider.GetSpotRateAsync("EUR", "EUR");
        rate.Should().Be(1.0m);
    }

    [Fact]
    public async Task GetSpotRateAsync_CrossRate_CalculatesExpectedRate()
    {
        // EUR to USD: 1.0850
        var eurToUsd = await _provider.GetSpotRateAsync("EUR", "USD");
        eurToUsd.Should().Be(1.0850m);

        // USD to EUR: 1 / 1.0850 = 0.921659
        var usdToEur = await _provider.GetSpotRateAsync("USD", "EUR");
        usdToEur.Should().Be(0.921659m);

        // PLN to USDC: 1 PLN = 0.2520 USD = 0.2520 USDC
        var plnToUsdc = await _provider.GetSpotRateAsync("PLN", "USDC");
        plnToUsdc.Should().Be(0.2520m);

        // EUR to USDC: 1.0850 USDC
        var eurToUsdc = await _provider.GetSpotRateAsync("EUR", "USDC");
        eurToUsdc.Should().Be(1.0850m);
    }

    [Fact]
    public async Task GetSpotRateAsync_UnsupportedCurrency_Throws()
    {
        var act = () => _provider.GetSpotRateAsync("XYZ", "USD");
        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*Unsupported base currency*XYZ*");
    }

    [Fact]
    public async Task SetRateToUsd_OverridesRateDynamically()
    {
        _provider.SetRateToUsd("EUR", 1.2000m);
        var rate = await _provider.GetSpotRateAsync("EUR", "USD");
        rate.Should().Be(1.2000m);
    }

    [Fact]
    public async Task GetRatesAsync_ReturnsRatesForSupportedCurrencies()
    {
        var rates = await _provider.GetRatesAsync("EUR");
        rates.Should().ContainKey("USD");
        rates.Should().ContainKey("USDC");
        rates.Should().ContainKey("PLN");
        rates["EUR"].Should().Be(1.0m);
    }
}
