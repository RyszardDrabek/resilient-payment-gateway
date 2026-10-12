using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PaymentGateway.Infrastructure.Options;
using PaymentGateway.Infrastructure.Persistence;
using PaymentGateway.Infrastructure.Services;
using Testcontainers.PostgreSql;
using Xunit;

namespace PaymentGateway.IntegrationTests;

public sealed class OutboxRetentionIntegrationTests : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private bool _dockerAvailable;

    public async Task InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("payment_outbox_retention_test")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await _postgres.StartAsync();
            _dockerAvailable = true;

            var options = new DbContextOptionsBuilder<PaymentDbContext>()
                .UseNpgsql(_postgres.GetConnectionString())
                .Options;
            await using var db = new PaymentDbContext(options);
            await db.Database.MigrateAsync();
        }
        catch
        {
            _dockerAvailable = false;
            if (_postgres is not null)
            {
                await _postgres.DisposeAsync().AsTask();
                _postgres = null;
            }
        }
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
        {
            await _postgres.DisposeAsync().AsTask();
        }
    }

    [Fact]
    public async Task CleanupExpiredOutboxMessagesAsync_PrunesProcessedMessagesOlderThanRetention_AndRetainsPendingAndRecent()
    {
        if (!_dockerAvailable || _postgres is null)
        {
            return;
        }

        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        await using var db = new PaymentDbContext(options);
        // Clear table
        await db.OutboxMessages.ExecuteDeleteAsync();

        var now = DateTimeOffset.UtcNow;

        var oldProcessed1 = new OutboxMessageRecord
        {
            EventType = "PaymentTransitioned",
            Payload = "{\"test\": 1}",
            CreatedAt = now.AddDays(-20),
            ProcessedAt = now.AddDays(-20),
            DeliveryAttempts = 1
        };

        var oldProcessed2 = new OutboxMessageRecord
        {
            EventType = "PaymentTransitioned",
            Payload = "{\"test\": 2}",
            CreatedAt = now.AddDays(-15),
            ProcessedAt = now.AddDays(-15),
            DeliveryAttempts = 1
        };

        var recentProcessed = new OutboxMessageRecord
        {
            EventType = "PaymentTransitioned",
            Payload = "{\"test\": 3}",
            CreatedAt = now.AddDays(-5),
            ProcessedAt = now.AddDays(-5),
            DeliveryAttempts = 1
        };

        // AC-2: Outbox records unread or pending dispatch (ProcessedAt == null) MUST be retained regardless of age
        var oldUnprocessed = new OutboxMessageRecord
        {
            EventType = "PaymentTransitioned",
            Payload = "{\"test\": 4}",
            CreatedAt = now.AddDays(-30),
            ProcessedAt = null,
            DeliveryAttempts = 0
        };

        var recentUnprocessed = new OutboxMessageRecord
        {
            EventType = "PaymentTransitioned",
            Payload = "{\"test\": 5}",
            CreatedAt = now.AddDays(-1),
            ProcessedAt = null,
            DeliveryAttempts = 0
        };

        db.OutboxMessages.AddRange(oldProcessed1, oldProcessed2, recentProcessed, oldUnprocessed, recentUnprocessed);
        await db.SaveChangesAsync();

        var outboxOptions = Microsoft.Extensions.Options.Options.Create(new OutboxOptions
        {
            RetentionDays = 14,
            RetentionCleanupIntervalHours = 24
        });

        var service = new OutboxRetentionService(db, outboxOptions, NullLogger<OutboxRetentionService>.Instance);

        // Act (AC-1, AC-3)
        var prunedCount = await service.CleanupExpiredOutboxMessagesAsync();

        // Assert
        prunedCount.Should().Be(2);

        var remainingMessages = await db.OutboxMessages.OrderBy(m => m.CreatedAt).ToListAsync();
        remainingMessages.Should().HaveCount(3);
        remainingMessages.Select(m => m.Id).Should().Contain([recentProcessed.Id, oldUnprocessed.Id, recentUnprocessed.Id]);
        remainingMessages.Select(m => m.Id).Should().NotContain([oldProcessed1.Id, oldProcessed2.Id]);
    }

    [Fact]
    public async Task CleanupExpiredOutboxMessagesAsync_CustomRetentionDays_AppliesCorrectCutoff()
    {
        if (!_dockerAvailable || _postgres is null)
        {
            return;
        }

        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        await using var db = new PaymentDbContext(options);
        await db.OutboxMessages.ExecuteDeleteAsync();

        var now = DateTimeOffset.UtcNow;

        var processed5DaysAgo = new OutboxMessageRecord
        {
            EventType = "PaymentTransitioned",
            Payload = "{\"test\": 1}",
            CreatedAt = now.AddDays(-5),
            ProcessedAt = now.AddDays(-5),
            DeliveryAttempts = 1
        };

        var processed2DaysAgo = new OutboxMessageRecord
        {
            EventType = "PaymentTransitioned",
            Payload = "{\"test\": 2}",
            CreatedAt = now.AddDays(-2),
            ProcessedAt = now.AddDays(-2),
            DeliveryAttempts = 1
        };

        db.OutboxMessages.AddRange(processed5DaysAgo, processed2DaysAgo);
        await db.SaveChangesAsync();

        var customOptions = Microsoft.Extensions.Options.Options.Create(new OutboxOptions
        {
            RetentionDays = 3,
            RetentionCleanupIntervalHours = 12
        });

        var service = new OutboxRetentionService(db, customOptions, NullLogger<OutboxRetentionService>.Instance);

        var prunedCount = await service.CleanupExpiredOutboxMessagesAsync();

        prunedCount.Should().Be(1);
        var remaining = await db.OutboxMessages.ToListAsync();
        remaining.Should().HaveCount(1);
        remaining[0].Id.Should().Be(processed2DaysAgo.Id);
    }

    [Fact]
    public async Task CleanupExpiredOutboxMessagesAsync_WithCustomTimeProvider_CalculatesCutoffFromTimeProvider()
    {
        if (!_dockerAvailable || _postgres is null)
        {
            return;
        }

        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        await using var db = new PaymentDbContext(options);
        await db.OutboxMessages.ExecuteDeleteAsync();

        var fixedBaseTime = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(fixedBaseTime);

        // Processed 15 days before the fixed time (retention 14 days -> pruned)
        var oldRecord = new OutboxMessageRecord
        {
            EventType = "PaymentTransitioned",
            Payload = "{\"test\": 1}",
            CreatedAt = fixedBaseTime.AddDays(-15),
            ProcessedAt = fixedBaseTime.AddDays(-15),
            DeliveryAttempts = 1
        };

        // Processed 10 days before the fixed time (within 14-day window -> retained)
        var recentRecord = new OutboxMessageRecord
        {
            EventType = "PaymentTransitioned",
            Payload = "{\"test\": 2}",
            CreatedAt = fixedBaseTime.AddDays(-10),
            ProcessedAt = fixedBaseTime.AddDays(-10),
            DeliveryAttempts = 1
        };

        db.OutboxMessages.AddRange(oldRecord, recentRecord);
        await db.SaveChangesAsync();

        var outboxOptions = Microsoft.Extensions.Options.Options.Create(new OutboxOptions
        {
            RetentionDays = 14,
            RetentionCleanupIntervalHours = 24
        });

        var service = new OutboxRetentionService(db, outboxOptions, NullLogger<OutboxRetentionService>.Instance, timeProvider);

        var prunedCount = await service.CleanupExpiredOutboxMessagesAsync();

        prunedCount.Should().Be(1);
        var remaining = await db.OutboxMessages.ToListAsync();
        remaining.Should().HaveCount(1);
        remaining[0].Id.Should().Be(recentRecord.Id);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
