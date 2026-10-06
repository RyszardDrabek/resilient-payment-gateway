using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Infrastructure.Persistence;
using PaymentGateway.Infrastructure.Repositories;
using Testcontainers.PostgreSql;
using Xunit;

namespace PaymentGateway.IntegrationTests;

public sealed class PaymentPersistenceTests : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private bool _dockerAvailable;

    public async Task InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("payment_persistence_test")
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
    public async Task PaymentRepository_AddAndGetById_PersistsAndLoadsAllFieldsCorrectly()
    {
        if (!_dockerAvailable || _postgres is null)
        {
            return;
        }

        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        await using var dbContext = new PaymentDbContext(options);
        var repository = new PaymentRepository(dbContext);

        var payment = Payment.Authorize(
            partyId: "merchant_persist_01",
            amount: 4999,
            currency: "EUR",
            settlementChannel: "MOCK",
            channelReference: "ref_persist_123");

        // Act - Save
        await repository.AddAsync(payment);

        // Act - Read using a separate context instance to verify actual database round-trip
        await using var readDbContext = new PaymentDbContext(options);
        var readRepository = new PaymentRepository(readDbContext);

        var loaded = await readRepository.GetByIdAsync(payment.Id);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(payment.Id);
        loaded.PartyId.Should().Be("merchant_persist_01");
        loaded.Amount.Should().Be(4999);
        loaded.Currency.Should().Be("EUR");
        loaded.SettlementChannel.Should().Be("MOCK");
        loaded.ChannelReference.Should().Be("ref_persist_123");
        loaded.DeclineReason.Should().BeNull();
        loaded.State.Should().Be(PaymentState.Authorized);
        loaded.CreatedAt.Should().BeCloseTo(payment.CreatedAt, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task PaymentRepository_AddDeclinedPayment_PersistsDeclineReason()
    {
        if (!_dockerAvailable || _postgres is null)
        {
            return;
        }

        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        await using var dbContext = new PaymentDbContext(options);
        var repository = new PaymentRepository(dbContext);

        var payment = Payment.Decline(
            partyId: "merchant_persist_02",
            amount: 1200,
            currency: "USD",
            settlementChannel: "ADYEN",
            channelReference: "ref_decline_456",
            declineReason: "Card expired");

        await repository.AddAsync(payment);

        await using var readDbContext = new PaymentDbContext(options);
        var readRepository = new PaymentRepository(readDbContext);

        var loaded = await readRepository.GetByIdAsync(payment.Id);

        loaded.Should().NotBeNull();
        loaded!.State.Should().Be(PaymentState.Declined);
        loaded.DeclineReason.Should().Be("Card expired");
        loaded.ChannelReference.Should().Be("ref_decline_456");
    }

    [Fact]
    public async Task PaymentRepository_GetById_NonExistent_ReturnsNull()
    {
        if (!_dockerAvailable || _postgres is null)
        {
            return;
        }

        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        await using var dbContext = new PaymentDbContext(options);
        var repository = new PaymentRepository(dbContext);

        var result = await repository.GetByIdAsync("pay_non_existent");

        result.Should().BeNull();
    }

    [Fact]
    public async Task IdempotencyRepository_AddAndFind_PersistsAndLoadsCorrectly()
    {
        if (!_dockerAvailable || _postgres is null)
        {
            return;
        }

        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        var record = IdempotencyRecord.CreateInFlight(
            key: "idem_persist_1",
            commandType: "Authorize",
            payloadHash: "hash_persist_123");

        await using (var dbContext = new PaymentDbContext(options))
        {
            var repository = new IdempotencyRepository(dbContext);
            await repository.AddAsync(record, CancellationToken.None);
        }

        await using (var readDbContext = new PaymentDbContext(options))
        {
            var readRepository = new IdempotencyRepository(readDbContext);
            var loaded = await readRepository.FindAsync("idem_persist_1", "Authorize", null, CancellationToken.None);

            loaded.Should().NotBeNull();
            loaded!.Key.Should().Be("idem_persist_1");
            loaded.CommandType.Should().Be("Authorize");
            loaded.PayloadHash.Should().Be("hash_persist_123");
            loaded.Status.Should().Be(PaymentGateway.Domain.Enums.IdempotencyStatus.InFlight);

            loaded.Complete(201, "{\"paymentId\":\"pay_100\"}", "pay_100");
            await readRepository.UpdateAsync(loaded, CancellationToken.None);
        }

        await using (var verifyDbContext = new PaymentDbContext(options))
        {
            var verifyRepository = new IdempotencyRepository(verifyDbContext);
            var completed = await verifyRepository.FindAsync("idem_persist_1", "Authorize", null, CancellationToken.None);

            completed.Should().NotBeNull();
            completed!.Status.Should().Be(PaymentGateway.Domain.Enums.IdempotencyStatus.Completed);
            completed.ResponseStatusCode.Should().Be(201);
            completed.ResponsePayload.Should().Be("{\"paymentId\":\"pay_100\"}");
            completed.PaymentId.Should().Be("pay_100");
        }
    }

    [Fact]
    public async Task IdempotencyRepository_DuplicateKey_ThrowsDbUpdateException()
    {
        if (!_dockerAvailable || _postgres is null)
        {
            return;
        }

        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        var record1 = IdempotencyRecord.CreateInFlight("idem_dup_key", "Authorize", "hash_1");
        var record2 = IdempotencyRecord.CreateInFlight("idem_dup_key", "Authorize", "hash_2");

        await using (var dbContext = new PaymentDbContext(options))
        {
            var repository = new IdempotencyRepository(dbContext);
            await repository.AddAsync(record1, CancellationToken.None);
        }

        await using (var duplicateDbContext = new PaymentDbContext(options))
        {
            var repository = new IdempotencyRepository(duplicateDbContext);
            var act = () => repository.AddAsync(record2, CancellationToken.None);
            await act.Should().ThrowAsync<PaymentGateway.Domain.Exceptions.IdempotencyInFlightException>();
        }
    }

    [Fact]
    public async Task PaymentRepository_UpdateAsync_ConcurrentModification_ThrowsPaymentConcurrencyException()
    {
        if (!_dockerAvailable || _postgres is null)
        {
            return;
        }

        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        var payment = Payment.Authorize(
            partyId: "merchant_concurrent_01",
            amount: 5000,
            currency: "EUR",
            settlementChannel: "MOCK",
            channelReference: "ref_init_1");

        await using (var db = new PaymentDbContext(options))
        {
            var repo = new PaymentRepository(db);
            await repo.AddAsync(payment);
        }

        // Load into two separate DbContext instances
        await using var db1 = new PaymentDbContext(options);
        await using var db2 = new PaymentDbContext(options);

        var repo1 = new PaymentRepository(db1);
        var repo2 = new PaymentRepository(db2);

        var p1 = await repo1.GetByIdAsync(payment.Id);
        var p2 = await repo2.GetByIdAsync(payment.Id);

        p1.Should().NotBeNull();
        p2.Should().NotBeNull();

        // Transition 1 succeeds (Version 1 -> 2)
        p1!.Capture("ref_cap_1");
        await repo1.UpdateAsync(p1);

        // Transition 2 attempts update with stale original version
        p2!.Capture("ref_cap_2");
        var act = () => repo2.UpdateAsync(p2);

        await act.Should().ThrowAsync<PaymentGateway.Domain.Exceptions.PaymentConcurrencyException>();
    }
}
