using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PaymentGateway.Infrastructure.Fx;
using PaymentGateway.Infrastructure.Options;
using PaymentGateway.Worker;
using Xunit;

namespace PaymentGateway.UnitTests.Worker;

public sealed class FxRateRefreshWorkerTests
{
    [Fact]
    public void FxRateRefreshOptions_HasExpectedDefaultValues()
    {
        var options = new FxRateRefreshOptions();

        options.Enabled.Should().BeTrue();
        options.IntervalSeconds.Should().Be(300);
    }

    [Fact]
    public async Task ProcessCycleAsync_UpdatesBaselineRatesInProvider()
    {
        var provider = new CachedFxRateProvider();
        // Modify a rate first
        provider.SetRateToUsd("EUR", 0.5000m);

        var options = Options.Create(new FxRateRefreshOptions { IntervalSeconds = 60 });
        var worker = new FxRateRefreshWorker(provider, options, NullLogger<FxRateRefreshWorker>.Instance);

        await worker.ProcessCycleAsync(CancellationToken.None);

        var eurToUsd = await provider.GetSpotRateAsync("EUR", "USD");
        eurToUsd.Should().Be(1.0850m);
    }
}
