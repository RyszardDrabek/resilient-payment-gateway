using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Ports;
using PaymentGateway.Infrastructure.Services;
using Xunit;

namespace PaymentGateway.UnitTests.Infrastructure.Services;

public sealed class IdempotencyRetentionServiceTests
{
    private sealed class TrackingIdempotencyRepository : IIdempotencyRepository
    {
        public DateTimeOffset? LastCleanupTime { get; private set; }
        public DateTimeOffset? LastReapTime { get; private set; }
        public int CleanupReturnCount { get; set; } = 5;
        public int ReapReturnCount { get; set; } = 2;

        public Task<IdempotencyRecord?> FindAsync(string key, string commandType, string? paymentId, CancellationToken ct) =>
            Task.FromResult<IdempotencyRecord?>(null);

        public Task AddAsync(IdempotencyRecord record, CancellationToken ct) => Task.CompletedTask;
        public Task UpdateAsync(IdempotencyRecord record, CancellationToken ct) => Task.CompletedTask;

        public Task<int> CleanupExpiredAsync(DateTimeOffset now, CancellationToken ct = default)
        {
            LastCleanupTime = now;
            return Task.FromResult(CleanupReturnCount);
        }

        public Task<int> ReapAbandonedLeasesAsync(DateTimeOffset now, CancellationToken ct = default)
        {
            LastReapTime = now;
            return Task.FromResult(ReapReturnCount);
        }
    }

    private sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    [Fact]
    public async Task CleanupExpiredRecordsAsync_PassesCurrentTimeToRepository_AndReturnsDeletedCount()
    {
        var fixedTime = new DateTimeOffset(2026, 10, 20, 15, 30, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(fixedTime);
        var repo = new TrackingIdempotencyRepository { CleanupReturnCount = 42 };
        var service = new IdempotencyRetentionService(repo, NullLogger<IdempotencyRetentionService>.Instance, timeProvider);

        var result = await service.CleanupExpiredRecordsAsync(CancellationToken.None);

        result.Should().Be(42);
        repo.LastCleanupTime.Should().Be(fixedTime);
    }

    [Fact]
    public async Task ReapAbandonedLeasesAsync_PassesCurrentTimeToRepository_AndReturnsReapedCount()
    {
        var fixedTime = new DateTimeOffset(2026, 10, 20, 15, 30, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(fixedTime);
        var repo = new TrackingIdempotencyRepository { ReapReturnCount = 7 };
        var service = new IdempotencyRetentionService(repo, NullLogger<IdempotencyRetentionService>.Instance, timeProvider);

        var result = await service.ReapAbandonedLeasesAsync(CancellationToken.None);

        result.Should().Be(7);
        repo.LastReapTime.Should().Be(fixedTime);
    }
}
