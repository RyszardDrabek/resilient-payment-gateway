using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.Fx;
using PaymentGateway.Infrastructure.Options;
using PaymentGateway.Worker;
using Xunit;

namespace PaymentGateway.UnitTests.Worker;

public sealed class FxQuoteExpirationWorkerTests
{
    [Fact]
    public void FxQuoteExpirationOptions_HasExpectedDefaultValues()
    {
        var options = new FxQuoteExpirationOptions();

        options.Enabled.Should().BeTrue();
        options.IntervalSeconds.Should().Be(30);
        options.BatchSize.Should().Be(1000);
    }

    [Fact]
    public async Task ProcessCycleAsync_CallsExpirationService_AndReturnsCount()
    {
        var fakeService = new FakeFxQuoteExpirationService(expiredCount: 8);
        var services = new ServiceCollection();
        services.AddScoped<IFxQuoteExpirationService>(_ => fakeService);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var options = Options.Create(new FxQuoteExpirationOptions { IntervalSeconds = 15 });
        var worker = new FxQuoteExpirationWorker(scopeFactory, options, NullLogger<FxQuoteExpirationWorker>.Instance);

        var expired = await worker.ProcessCycleAsync(CancellationToken.None);

        expired.Should().Be(8);
        fakeService.ExpireCallCount.Should().Be(1);
    }

    [Fact]
    public async Task ProcessCycleAsync_WhenExceptionOccurs_PropagatesToWorkerCycle()
    {
        var failingService = new FailingFxQuoteExpirationService();
        var services = new ServiceCollection();
        services.AddScoped<IFxQuoteExpirationService>(_ => failingService);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var options = Options.Create(new FxQuoteExpirationOptions { IntervalSeconds = 15 });
        var worker = new FxQuoteExpirationWorker(scopeFactory, options, NullLogger<FxQuoteExpirationWorker>.Instance);

        var act = () => worker.ProcessCycleAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Database connectivity failure during FX quote expiration cycle.");
    }

    private sealed class FakeFxQuoteExpirationService(int expiredCount) : IFxQuoteExpirationService
    {
        public int ExpireCallCount { get; private set; }

        public Task<int> ExpireStaleQuotesAsync(CancellationToken ct = default)
        {
            ExpireCallCount++;
            return Task.FromResult(expiredCount);
        }
    }

    private sealed class FailingFxQuoteExpirationService : IFxQuoteExpirationService
    {
        public Task<int> ExpireStaleQuotesAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("Database connectivity failure during FX quote expiration cycle.");
    }
}
