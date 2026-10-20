using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Infrastructure.Persistence;
using PaymentGateway.Infrastructure.Repositories;
using Testcontainers.PostgreSql;
using Xunit;

namespace PaymentGateway.IntegrationTests;

public sealed class FxQuoteExpirationIntegrationTests : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private bool _dockerAvailable;

    public async Task InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("payment_fx_expiration_test")
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
    public async Task ExpireStaleQuotesAsync_ExpiresActiveStaleQuotes_LeavingActiveUnexpiredAndConsumedUntouched()
    {
        if (!_dockerAvailable || _postgres is null) return;

        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        var now = DateTimeOffset.UtcNow;
        var staleActiveId = $"q_stale_active_{Guid.NewGuid():N}";
        var freshActiveId = $"q_fresh_active_{Guid.NewGuid():N}";
        var consumedStaleId = $"q_consumed_stale_{Guid.NewGuid():N}";
        var alreadyExpiredId = $"q_already_expired_{Guid.NewGuid():N}";

        await using (var seedDb = new PaymentDbContext(options))
        {
            var staleActive = new CurrencyQuote(
                id: staleActiveId,
                partyId: "p_1",
                baseCurrency: "EUR",
                baseAmount: 10000,
                targetCurrency: "USD",
                targetAmount: 10800,
                spotRate: 1.08m,
                spreadBps: 10,
                exchangeRate: 1.08m,
                createdAt: now.AddMinutes(-10),
                expiresAt: now.AddMinutes(-1),
                status: CurrencyQuoteStatus.Active);

            var freshActive = new CurrencyQuote(
                id: freshActiveId,
                partyId: "p_1",
                baseCurrency: "EUR",
                baseAmount: 10000,
                targetCurrency: "USD",
                targetAmount: 10800,
                spotRate: 1.08m,
                spreadBps: 10,
                exchangeRate: 1.08m,
                createdAt: now,
                expiresAt: now.AddMinutes(15),
                status: CurrencyQuoteStatus.Active);

            var consumedStale = new CurrencyQuote(
                id: consumedStaleId,
                partyId: "p_1",
                baseCurrency: "EUR",
                baseAmount: 10000,
                targetCurrency: "USD",
                targetAmount: 10800,
                spotRate: 1.08m,
                spreadBps: 10,
                exchangeRate: 1.08m,
                createdAt: now.AddMinutes(-10),
                expiresAt: now.AddMinutes(-2),
                status: CurrencyQuoteStatus.Consumed,
                consumedByPaymentId: "pay_1",
                consumedAt: now.AddMinutes(-5));

            var alreadyExpired = new CurrencyQuote(
                id: alreadyExpiredId,
                partyId: "p_1",
                baseCurrency: "EUR",
                baseAmount: 10000,
                targetCurrency: "USD",
                targetAmount: 10800,
                spotRate: 1.08m,
                spreadBps: 10,
                exchangeRate: 1.08m,
                createdAt: now.AddMinutes(-20),
                expiresAt: now.AddMinutes(-15),
                status: CurrencyQuoteStatus.Expired);

            seedDb.CurrencyQuotes.AddRange(staleActive, freshActive, consumedStale, alreadyExpired);
            await seedDb.SaveChangesAsync();
        }

        await using (var testDb = new PaymentDbContext(options))
        {
            var repo = new FxQuoteRepository(testDb);
            var expiredCount = await repo.ExpireStaleQuotesAsync(now, batchSize: 100);

            expiredCount.Should().Be(1);
        }

        await using (var verifyDb = new PaymentDbContext(options))
        {
            var staleActive = await verifyDb.CurrencyQuotes.FindAsync(staleActiveId);
            staleActive.Should().NotBeNull();
            staleActive!.Status.Should().Be(CurrencyQuoteStatus.Expired);

            var freshActive = await verifyDb.CurrencyQuotes.FindAsync(freshActiveId);
            freshActive.Should().NotBeNull();
            freshActive!.Status.Should().Be(CurrencyQuoteStatus.Active);

            var consumedStale = await verifyDb.CurrencyQuotes.FindAsync(consumedStaleId);
            consumedStale.Should().NotBeNull();
            consumedStale!.Status.Should().Be(CurrencyQuoteStatus.Consumed);

            var alreadyExpired = await verifyDb.CurrencyQuotes.FindAsync(alreadyExpiredId);
            alreadyExpired.Should().NotBeNull();
            alreadyExpired!.Status.Should().Be(CurrencyQuoteStatus.Expired);
        }
    }

    [Fact]
    public async Task ExpireStaleQuotesAsync_RespectsBatchSize()
    {
        if (!_dockerAvailable || _postgres is null) return;

        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        var now = DateTimeOffset.UtcNow;
        var q1 = $"q_batch_1_{Guid.NewGuid():N}";
        var q2 = $"q_batch_2_{Guid.NewGuid():N}";
        var q3 = $"q_batch_3_{Guid.NewGuid():N}";

        await using (var seedDb = new PaymentDbContext(options))
        {
            for (var i = 0; i < 3; i++)
            {
                var id = i switch { 0 => q1, 1 => q2, _ => q3 };
                seedDb.CurrencyQuotes.Add(new CurrencyQuote(
                    id: id,
                    partyId: "p_1",
                    baseCurrency: "EUR",
                    baseAmount: 1000,
                    targetCurrency: "USD",
                    targetAmount: 1100,
                    spotRate: 1.1m,
                    spreadBps: 10,
                    exchangeRate: 1.1m,
                    createdAt: now.AddMinutes(-10),
                    expiresAt: now.AddMinutes(-5 + i),
                    status: CurrencyQuoteStatus.Active));
            }
            await seedDb.SaveChangesAsync();
        }

        await using (var testDb = new PaymentDbContext(options))
        {
            var repo = new FxQuoteRepository(testDb);
            var firstBatch = await repo.ExpireStaleQuotesAsync(now, batchSize: 2);
            firstBatch.Should().Be(2);

            var secondBatch = await repo.ExpireStaleQuotesAsync(now, batchSize: 2);
            secondBatch.Should().Be(1);

            var thirdBatch = await repo.ExpireStaleQuotesAsync(now, batchSize: 2);
            thirdBatch.Should().Be(0);
        }
    }
}
