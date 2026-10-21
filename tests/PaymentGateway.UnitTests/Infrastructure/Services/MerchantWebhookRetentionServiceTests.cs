using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Ports;
using PaymentGateway.Infrastructure.Options;
using PaymentGateway.Infrastructure.Services;
using Xunit;

namespace PaymentGateway.UnitTests.Infrastructure.Services;

public sealed class MerchantWebhookRetentionServiceTests
{
    private sealed class TrackingDeadLetterRepository : IMerchantWebhookDeadLetterRepository
    {
        public DateTimeOffset? LastCutoff { get; private set; }
        public int ReturnCount { get; set; } = 7;

        public Task AddAsync(MerchantWebhookDeadLetter deadLetter, CancellationToken ct = default) => Task.CompletedTask;
        public Task<MerchantWebhookDeadLetter?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<MerchantWebhookDeadLetter?>(null);
        public Task<IReadOnlyList<MerchantWebhookDeadLetter>> ListByMerchantAsync(string merchantId, string? status = null, int page = 1, int pageSize = 50, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MerchantWebhookDeadLetter>>([]);
        public Task UpdateAsync(MerchantWebhookDeadLetter deadLetter, CancellationToken ct = default) => Task.CompletedTask;

        public Task<int> CleanupExpiredAsync(DateTimeOffset cutoff, CancellationToken ct = default)
        {
            LastCutoff = cutoff;
            return Task.FromResult(ReturnCount);
        }
    }

    private sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    [Fact]
    public async Task CleanupExpiredDeadLettersAsync_CalculatesCorrectCutoff_AndReturnsDeletedCount()
    {
        var fixedTime = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(fixedTime);
        var repo = new TrackingDeadLetterRepository { ReturnCount = 14 };
        var options = Options.Create(new MerchantWebhookOptions { DeadLetterRetentionDays = 60 });
        var service = new MerchantWebhookRetentionService(repo, options, NullLogger<MerchantWebhookRetentionService>.Instance, timeProvider);

        var count = await service.CleanupExpiredDeadLettersAsync(CancellationToken.None);

        count.Should().Be(14);
        repo.LastCutoff.Should().Be(fixedTime.AddDays(-60));
    }

    [Fact]
    public async Task CleanupExpiredDeadLettersAsync_WhenRetentionDaysZeroOrNegative_DefaultsTo90Days()
    {
        var fixedTime = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(fixedTime);
        var repo = new TrackingDeadLetterRepository { ReturnCount = 3 };
        var options = Options.Create(new MerchantWebhookOptions { DeadLetterRetentionDays = 0 });
        var service = new MerchantWebhookRetentionService(repo, options, NullLogger<MerchantWebhookRetentionService>.Instance, timeProvider);

        var count = await service.CleanupExpiredDeadLettersAsync(CancellationToken.None);

        count.Should().Be(3);
        repo.LastCutoff.Should().Be(fixedTime.AddDays(-90));
    }
}
