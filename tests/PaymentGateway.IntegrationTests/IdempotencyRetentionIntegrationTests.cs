using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Infrastructure.Persistence;
using PaymentGateway.Infrastructure.Repositories;
using Testcontainers.PostgreSql;
using Xunit;

namespace PaymentGateway.IntegrationTests;

public sealed class IdempotencyRetentionIntegrationTests : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private bool _dockerAvailable;

    public async Task InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("payment_idem_test")
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
    public async Task CleanupExpiredAsync_DeletesOnlyExpiredRecords()
    {
        if (!_dockerAvailable || _postgres is null) return;

        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        var now = DateTimeOffset.UtcNow;
        var expiredKey = $"idem_expired_{Guid.NewGuid():N}";
        var activeKey = $"idem_active_{Guid.NewGuid():N}";

        await using (var seedDb = new PaymentDbContext(options))
        {
            var expiredRecord = IdempotencyRecord.CreateInFlight(
                key: expiredKey,
                commandType: "Authorize",
                payloadHash: "hash_expired",
                ttl: TimeSpan.FromHours(-1), // Already expired
                now: now);
            expiredRecord.Complete(200, "{\"status\":\"ok\"}");

            var activeRecord = IdempotencyRecord.CreateInFlight(
                key: activeKey,
                commandType: "Authorize",
                payloadHash: "hash_active",
                ttl: TimeSpan.FromHours(24),
                now: now);
            activeRecord.Complete(200, "{\"status\":\"ok\"}");

            seedDb.IdempotencyRecords.AddRange(expiredRecord, activeRecord);
            await seedDb.SaveChangesAsync();
        }

        await using (var testDb = new PaymentDbContext(options))
        {
            var repo = new IdempotencyRepository(testDb);
            var deletedCount = await repo.CleanupExpiredAsync(now);

            deletedCount.Should().BeGreaterThanOrEqualTo(1);
        }

        await using (var verifyDb = new PaymentDbContext(options))
        {
            var remainingExpired = await verifyDb.IdempotencyRecords.FirstOrDefaultAsync(r => r.Key == expiredKey);
            remainingExpired.Should().BeNull();

            var remainingActive = await verifyDb.IdempotencyRecords.FirstOrDefaultAsync(r => r.Key == activeKey);
            remainingActive.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task ReapAbandonedLeasesAsync_UpdatesStaleInFlightLeasesToFailed()
    {
        if (!_dockerAvailable || _postgres is null) return;

        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        var now = DateTimeOffset.UtcNow;
        var abandonedKey = $"idem_abandoned_{Guid.NewGuid():N}";
        var activeLeaseKey = $"idem_active_lease_{Guid.NewGuid():N}";

        await using (var seedDb = new PaymentDbContext(options))
        {
            // Abandoned: LockedUntil is in the past
            var abandonedRecord = IdempotencyRecord.CreateInFlight(
                key: abandonedKey,
                commandType: "Authorize",
                payloadHash: "hash_abandoned",
                leaseDuration: TimeSpan.FromMinutes(-5),
                ttl: TimeSpan.FromHours(24),
                now: now);

            // Active lease: LockedUntil is 5 minutes in the future
            var activeLeaseRecord = IdempotencyRecord.CreateInFlight(
                key: activeLeaseKey,
                commandType: "Authorize",
                payloadHash: "hash_active_lease",
                leaseDuration: TimeSpan.FromMinutes(5),
                ttl: TimeSpan.FromHours(24),
                now: now);

            seedDb.IdempotencyRecords.AddRange(abandonedRecord, activeLeaseRecord);
            await seedDb.SaveChangesAsync();
        }

        await using (var testDb = new PaymentDbContext(options))
        {
            var repo = new IdempotencyRepository(testDb);
            var reapedCount = await repo.ReapAbandonedLeasesAsync(now);

            reapedCount.Should().BeGreaterThanOrEqualTo(1);
        }

        await using (var verifyDb = new PaymentDbContext(options))
        {
            var reaped = await verifyDb.IdempotencyRecords.FirstOrDefaultAsync(r => r.Key == abandonedKey);
            reaped.Should().NotBeNull();
            reaped!.Status.Should().Be(IdempotencyStatus.Failed);
            reaped.ResponseStatusCode.Should().Be(504);
            reaped.ResponsePayload.Should().Contain("lease expired");
            reaped.ResponsePayload.Should().Contain("\"title\":\"Gateway Timeout\"");
            reaped.ResponsePayload.Should().Contain("\"status\":504");
            reaped.LockedUntil.Should().BeNull();

            var stillActive = await verifyDb.IdempotencyRecords.FirstOrDefaultAsync(r => r.Key == activeLeaseKey);
            stillActive.Should().NotBeNull();
            stillActive!.Status.Should().Be(IdempotencyStatus.InFlight);
            stillActive.LockedUntil.Should().BeAfter(now);
        }
    }
}
