using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.Idempotency;
using PaymentGateway.Infrastructure.Options;
using PaymentGateway.Worker;
using Xunit;

namespace PaymentGateway.UnitTests.Worker;

public sealed class IdempotencyRetentionCleanupWorkerTests
{
    [Fact]
    public void IdempotencyRetentionOptions_HasExpectedDefaultValues()
    {
        var options = new IdempotencyRetentionOptions();

        options.Enabled.Should().BeTrue();
        options.CleanupIntervalMinutes.Should().Be(15);
    }

    [Fact]
    public async Task ProcessCycleAsync_CallsRetentionService_AndReturnsCounts()
    {
        var fakeService = new FakeIdempotencyRetentionService(deletedCount: 12, reapedCount: 3);
        var services = new ServiceCollection();
        services.AddScoped<IIdempotencyRetentionService>(_ => fakeService);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var options = Options.Create(new IdempotencyRetentionOptions { CleanupIntervalMinutes = 10 });
        var worker = new IdempotencyRetentionCleanupWorker(scopeFactory, options, NullLogger<IdempotencyRetentionCleanupWorker>.Instance);

        var (pruned, reaped) = await worker.ProcessCycleAsync(CancellationToken.None);

        pruned.Should().Be(12);
        reaped.Should().Be(3);
        fakeService.CleanupCallCount.Should().Be(1);
        fakeService.ReapCallCount.Should().Be(1);
    }

    [Fact]
    public async Task ProcessCycleAsync_WhenExceptionOccurs_PropagatesToWorkerCycle()
    {
        var failingService = new FailingIdempotencyRetentionService();
        var services = new ServiceCollection();
        services.AddScoped<IIdempotencyRetentionService>(_ => failingService);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var options = Options.Create(new IdempotencyRetentionOptions { CleanupIntervalMinutes = 10 });
        var worker = new IdempotencyRetentionCleanupWorker(scopeFactory, options, NullLogger<IdempotencyRetentionCleanupWorker>.Instance);

        var act = () => worker.ProcessCycleAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Database connectivity failure during retention cycle.");
    }

    private sealed class FakeIdempotencyRetentionService(int deletedCount, int reapedCount) : IIdempotencyRetentionService
    {
        public int CleanupCallCount { get; private set; }
        public int ReapCallCount { get; private set; }

        public Task<int> CleanupExpiredRecordsAsync(CancellationToken ct = default)
        {
            CleanupCallCount++;
            return Task.FromResult(deletedCount);
        }

        public Task<int> ReapAbandonedLeasesAsync(CancellationToken ct = default)
        {
            ReapCallCount++;
            return Task.FromResult(reapedCount);
        }
    }

    private sealed class FailingIdempotencyRetentionService : IIdempotencyRetentionService
    {
        public Task<int> CleanupExpiredRecordsAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("Database connectivity failure during retention cycle.");

        public Task<int> ReapAbandonedLeasesAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("Database connectivity failure during retention cycle.");
    }
}
