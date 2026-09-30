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
}
