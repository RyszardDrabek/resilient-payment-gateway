using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PaymentGateway.Application.Events;
using PaymentGateway.Infrastructure.Consumers;
using PaymentGateway.Infrastructure.Fx;
using Xunit;

namespace PaymentGateway.UnitTests.Infrastructure;

public sealed class FxRatesUpdatedConsumerTests
{
    [Fact]
    public async Task ProcessEventAsync_UpdatesRatesInCachedFxRateProvider()
    {
        var provider = new CachedFxRateProvider();
        provider.SetRateToUsd("EUR", 0.5000m);

        var consumer = new FxRatesUpdatedConsumer(provider, NullLogger<FxRatesUpdatedConsumer>.Instance);

        var freshRates = new Dictionary<string, decimal>
        {
            ["EUR"] = 1.0950m,
            ["PLN"] = 0.2600m
        };
        var evt = new FxRatesRefreshedEvent(freshRates, DateTimeOffset.UtcNow);

        await consumer.ProcessEventAsync(evt);

        var rate = await provider.GetSpotRateAsync("EUR", "USD");
        rate.Should().Be(1.0950m);

        var plnRate = await provider.GetSpotRateAsync("PLN", "USD");
        plnRate.Should().Be(0.2600m);
    }
}
