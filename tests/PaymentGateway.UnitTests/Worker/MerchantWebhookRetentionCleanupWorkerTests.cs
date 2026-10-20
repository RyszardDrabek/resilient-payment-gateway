using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.Webhooks;
using PaymentGateway.Infrastructure.Options;
using PaymentGateway.Worker;
using Xunit;

namespace PaymentGateway.UnitTests.Worker;

public sealed class MerchantWebhookRetentionCleanupWorkerTests
{
    [Fact]
    public void MerchantWebhookOptions_RetentionProperties_HaveExpectedDefaults()
    {
        var options = new MerchantWebhookOptions();

        options.DeadLetterRetentionDays.Should().Be(90);
        options.RetentionCleanupIntervalHours.Should().Be(24);
    }

    [Fact]
    public async Task ProcessCycleAsync_CallsRetentionService_AndReturnsCount()
    {
        var fakeService = new FakeMerchantWebhookRetentionService(prunedCount: 11);
        var services = new ServiceCollection();
        services.AddScoped<IMerchantWebhookRetentionService>(_ => fakeService);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var options = Options.Create(new MerchantWebhookOptions { RetentionCleanupIntervalHours = 12 });
        var worker = new MerchantWebhookRetentionCleanupWorker(scopeFactory, options, NullLogger<MerchantWebhookRetentionCleanupWorker>.Instance);

        var pruned = await worker.ProcessCycleAsync(CancellationToken.None);

        pruned.Should().Be(11);
        fakeService.CleanupCallCount.Should().Be(1);
    }

    [Fact]
    public async Task ProcessCycleAsync_WhenExceptionOccurs_PropagatesToWorkerCycle()
    {
        var failingService = new FailingMerchantWebhookRetentionService();
        var services = new ServiceCollection();
        services.AddScoped<IMerchantWebhookRetentionService>(_ => failingService);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var options = Options.Create(new MerchantWebhookOptions { RetentionCleanupIntervalHours = 12 });
        var worker = new MerchantWebhookRetentionCleanupWorker(scopeFactory, options, NullLogger<MerchantWebhookRetentionCleanupWorker>.Instance);

        var act = () => worker.ProcessCycleAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Database connectivity failure during merchant webhook DLQ cleanup.");
    }

    private sealed class FakeMerchantWebhookRetentionService(int prunedCount) : IMerchantWebhookRetentionService
    {
        public int CleanupCallCount { get; private set; }

        public Task<int> CleanupExpiredDeadLettersAsync(CancellationToken ct = default)
        {
            CleanupCallCount++;
            return Task.FromResult(prunedCount);
        }
    }

    private sealed class FailingMerchantWebhookRetentionService : IMerchantWebhookRetentionService
    {
        public Task<int> CleanupExpiredDeadLettersAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("Database connectivity failure during merchant webhook DLQ cleanup.");
    }
}
