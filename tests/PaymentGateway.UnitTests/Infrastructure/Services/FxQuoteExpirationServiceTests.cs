using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Ports;
using PaymentGateway.Infrastructure.Options;
using PaymentGateway.Infrastructure.Services;
using Xunit;

namespace PaymentGateway.UnitTests.Infrastructure.Services;

public sealed class FxQuoteExpirationServiceTests
{
    private sealed class TrackingFxQuoteRepository : IFxQuoteRepository
    {
        public DateTimeOffset? LastExpirationTime { get; private set; }
        public int LastBatchSize { get; private set; }
        public int ExpiredReturnCount { get; set; } = 5;

        public Task<CurrencyQuote?> GetByIdAsync(string quoteId, CancellationToken ct = default) =>
            Task.FromResult<CurrencyQuote?>(null);

        public Task AddAsync(CurrencyQuote quote, CancellationToken ct = default) => Task.CompletedTask;

        public Task UpdateAsync(CurrencyQuote quote, CancellationToken ct = default) => Task.CompletedTask;

        public Task<int> ExpireStaleQuotesAsync(DateTimeOffset now, int batchSize = 1000, CancellationToken ct = default)
        {
            LastExpirationTime = now;
            LastBatchSize = batchSize;
            return Task.FromResult(ExpiredReturnCount);
        }
    }

    private sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    [Fact]
    public async Task ExpireStaleQuotesAsync_PassesCurrentTimeAndBatchSizeToRepository_AndReturnsExpiredCount()
    {
        var fixedTime = new DateTimeOffset(2026, 10, 20, 16, 45, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(fixedTime);
        var repo = new TrackingFxQuoteRepository { ExpiredReturnCount = 19 };
        var options = Options.Create(new FxQuoteExpirationOptions { BatchSize = 500 });
        var service = new FxQuoteExpirationService(repo, options, NullLogger<FxQuoteExpirationService>.Instance, timeProvider);

        var result = await service.ExpireStaleQuotesAsync(CancellationToken.None);

        result.Should().Be(19);
        repo.LastExpirationTime.Should().Be(fixedTime);
        repo.LastBatchSize.Should().Be(500);
    }

    [Fact]
    public async Task ExpireStaleQuotesAsync_WhenBatchSizeIsZeroOrNegative_DefaultsTo1000()
    {
        var fixedTime = new DateTimeOffset(2026, 10, 20, 16, 45, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(fixedTime);
        var repo = new TrackingFxQuoteRepository { ExpiredReturnCount = 3 };
        var options = Options.Create(new FxQuoteExpirationOptions { BatchSize = 0 });
        var service = new FxQuoteExpirationService(repo, options, NullLogger<FxQuoteExpirationService>.Instance, timeProvider);

        var result = await service.ExpireStaleQuotesAsync(CancellationToken.None);

        result.Should().Be(3);
        repo.LastBatchSize.Should().Be(1000);
    }
}
