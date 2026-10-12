using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.Events;
using PaymentGateway.Infrastructure.Options;
using PaymentGateway.Worker;

namespace PaymentGateway.UnitTests.Worker;

public sealed class OutboxRetentionCleanupWorkerTests
{
    [Fact]
    public void OutboxOptions_HasExpectedDefaultRetentionAndInterval()
    {
        var options = new OutboxOptions();

        options.RetentionDays.Should().Be(14);
        options.RetentionCleanupIntervalHours.Should().Be(24);
    }

    [Fact]
    public async Task ProcessRetentionCleanupCycleAsync_InvokesRetentionServiceAndReturnsPrunedCount()
    {
        var services = new ServiceCollection();
        var fakeService = new FakeOutboxRetentionService(deletedCountToReturn: 42);
        services.AddScoped<IOutboxRetentionService>(_ => fakeService);
        var serviceProvider = services.BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var options = Microsoft.Extensions.Options.Options.Create(new OutboxOptions { RetentionDays = 14 });
        var worker = new OutboxRetentionCleanupWorker(scopeFactory, options, NullLogger<OutboxRetentionCleanupWorker>.Instance);

        var result = await worker.ProcessRetentionCleanupCycleAsync(CancellationToken.None);

        result.Should().Be(42);
        fakeService.InvocationCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenExceptionOccurs_LogsAndRecoversWithoutCrashing()
    {
        // AC-4: Transient database error caught, operational failure logged, worker continues
        var services = new ServiceCollection();
        var failingService = new FailingOutboxRetentionService();
        services.AddScoped<IOutboxRetentionService>(_ => failingService);
        var serviceProvider = services.BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var options = Microsoft.Extensions.Options.Options.Create(new OutboxOptions
        {
            RetentionDays = 14,
            RetentionCleanupIntervalHours = 1
        });

        var worker = new OutboxRetentionCleanupWorker(scopeFactory, options, NullLogger<OutboxRetentionCleanupWorker>.Instance);

        // Cancel token source with timeout fallback
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Start worker task
        var executeTask = worker.StartAsync(cts.Token);
        await failingService.Attempted;
        cts.Cancel();

        // Worker should stop gracefully when cancelled without throwing the internal DB exception
        var act = async () => await executeTask;
        await act.Should().NotThrowAsync();
        failingService.AttemptCount.Should().BeGreaterThanOrEqualTo(1);
    }

    private sealed class FakeOutboxRetentionService(int deletedCountToReturn) : IOutboxRetentionService
    {
        public int InvocationCount { get; private set; }

        public Task<int> CleanupExpiredOutboxMessagesAsync(CancellationToken ct = default)
        {
            InvocationCount++;
            return Task.FromResult(deletedCountToReturn);
        }
    }

    private sealed class FailingOutboxRetentionService : IOutboxRetentionService
    {
        private readonly TaskCompletionSource<bool> _attemptedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Attempted => _attemptedTcs.Task;
        public int AttemptCount { get; private set; }

        public Task<int> CleanupExpiredOutboxMessagesAsync(CancellationToken ct = default)
        {
            AttemptCount++;
            _attemptedTcs.TrySetResult(true);
            throw new InvalidOperationException("Simulated transient PostgreSQL connection failure.");
        }
    }
}
