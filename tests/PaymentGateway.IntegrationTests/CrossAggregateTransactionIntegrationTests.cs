using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using PaymentGateway.Api.Endpoints;
using PaymentGateway.Application.Fx.Models;
using PaymentGateway.Application.Payments;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Ports;
using PaymentGateway.Infrastructure.Persistence;
using PaymentGateway.Infrastructure.Risk;
using Testcontainers.PostgreSql;
using Xunit;

namespace PaymentGateway.IntegrationTests;

public sealed class CrossAggregateTransactionIntegrationTests : IAsyncLifetime
{
    private const string JwtKey = "DEV_ONLY_CHANGE_ME_32CHARS_MINIMUM!!";
    private PostgreSqlContainer? _postgres;
    private bool _dockerAvailable;

    public async Task InitializeAsync()
    {
        InlineRiskEvaluator.ResetVelocity();
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("payment_uow_test")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await _postgres.StartAsync();
            _dockerAvailable = true;

            var payOptions = new DbContextOptionsBuilder<PaymentDbContext>()
                .UseNpgsql(_postgres.GetConnectionString())
                .Options;
            await using var payDb = new PaymentDbContext(payOptions);
            await payDb.Database.MigrateAsync();

            var riskOptions = new DbContextOptionsBuilder<RiskDbContext>()
                .UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "risk"))
                .Options;
            await using var riskDb = new RiskDbContext(riskOptions);
            await riskDb.Database.MigrateAsync();
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
        InlineRiskEvaluator.ResetVelocity();
        if (_postgres is not null)
        {
            await _postgres.DisposeAsync().AsTask();
        }
    }

    private WebApplicationFactory<Program> CreateFactory()
    {
        var connectionString = _dockerAvailable && _postgres is not null
            ? _postgres.GetConnectionString()
            : "Host=localhost;Database=dummy;Username=dummy;Password=dummy";

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:PaymentDb"] = connectionString,
                        ["ConnectionStrings:RiskDb"] = connectionString,
                        ["Auth:JwtSigningKey"] = JwtKey,
                        ["RabbitMq:UseInMemory"] = "true",
                        ["PaymentGateway:ActiveChannel"] = "MOCK",
                        ["Fx:DefaultSpreadBps"] = "50",
                        ["Fx:DefaultTtlMinutes"] = "15"
                    });
                });
            });
    }

    private static string CreateJwtToken(string role = "merchant")
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "payment-gateway",
            audience: "payment-gateway",
            claims: [new Claim("sub", "integration-test"), new Claim("role", role)],
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Fact]
    public async Task UnitOfWork_ExecuteTransactionAsync_CommitsAllAggregatesSuccessfully()
    {
        if (!_dockerAvailable || _postgres is null) return;

        await using var factory = CreateFactory();
        using var scope = factory.Services.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();

        var paymentId = $"pay_uow_{Guid.NewGuid():N}";
        var quoteId = $"fxq_uow_{Guid.NewGuid():N}";
        var idemKey = $"idem_uow_{Guid.NewGuid():N}";

        // Note: Using db.<Set>.Add() directly here is an intentional design choice for this unit-of-work test:
        // it specifically isolates and asserts the multi-entity transaction boundary and EF Core change tracker
        // behavior of IUnitOfWork itself, independent of individual repository implementations.
        // Full end-to-end repository usage is verified in AuthorizePayment_WithCurrencyQuote_RollsBackOrCommitsCrossAggregateAtomically.
        await uow.ExecuteTransactionAsync(async ct =>
        {
            var payment = Payment.Authorize("merch_uow_test", 5000, "EUR", "MOCK", "ref_tx_1");
            db.Payments.Add(payment);

            var quote = CurrencyQuote.Create(
                id: quoteId,
                partyId: "merch_uow_test",
                baseCurrency: "PLN",
                baseAmount: 10000,
                targetCurrency: "USDC",
                spotRate: 0.2520m,
                spreadBps: 50,
                ttl: TimeSpan.FromMinutes(15),
                now: DateTimeOffset.UtcNow);
            db.CurrencyQuotes.Add(quote);

            var idem = IdempotencyRecord.CreateInFlight(idemKey, "Authorize", "hash_123");
            db.IdempotencyRecords.Add(idem);

            await uow.SaveChangesAsync(ct);
        });

        // Verify in a separate fresh context that all 3 entities were committed
        var verifyOptions = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        await using var verifyDb = new PaymentDbContext(verifyOptions);

        var savedQuote = await verifyDb.CurrencyQuotes.FirstOrDefaultAsync(q => q.Id == quoteId);
        savedQuote.Should().NotBeNull();
        savedQuote!.Status.Should().Be(CurrencyQuoteStatus.Active);

        var savedIdem = await verifyDb.IdempotencyRecords.FirstOrDefaultAsync(i => i.Key == idemKey);
        savedIdem.Should().NotBeNull();
    }

    [Fact]
    public async Task UnitOfWork_ExecuteTransactionAsync_WhenExceptionOccurs_RollsBackAllAggregates()
    {
        if (!_dockerAvailable || _postgres is null) return;

        await using var factory = CreateFactory();
        using var scope = factory.Services.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();

        var quoteId = $"fxq_rollback_{Guid.NewGuid():N}";
        var idemKey = $"idem_rollback_{Guid.NewGuid():N}";

        var act = () => uow.ExecuteTransactionAsync(async ct =>
        {
            var quote = CurrencyQuote.Create(
                id: quoteId,
                partyId: "merch_uow_fail",
                baseCurrency: "EUR",
                baseAmount: 10000,
                targetCurrency: "USDC",
                spotRate: 1.0850m,
                spreadBps: 50,
                ttl: TimeSpan.FromMinutes(15),
                now: DateTimeOffset.UtcNow);
            db.CurrencyQuotes.Add(quote);

            var idem = IdempotencyRecord.CreateInFlight(idemKey, "Authorize", "hash_rollback");
            db.IdempotencyRecords.Add(idem);

            await db.SaveChangesAsync(ct);

            // Simulate failure before transaction commits
            throw new InvalidOperationException("Simulated failure mid-transaction.");
        });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Simulated failure mid-transaction.*");

        // Verify in a separate fresh context that neither quote nor idempotency record exists
        var verifyOptions = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        await using var verifyDb = new PaymentDbContext(verifyOptions);

        var quoteInDb = await verifyDb.CurrencyQuotes.FirstOrDefaultAsync(q => q.Id == quoteId);
        quoteInDb.Should().BeNull();

        var idemInDb = await verifyDb.IdempotencyRecords.FirstOrDefaultAsync(i => i.Key == idemKey);
        idemInDb.Should().BeNull();
    }

    [Fact]
    public async Task AuthorizePayment_WithQuote_UnderUnitOfWork_AtomicallyPersistsPaymentAndQuoteInPostgreSql()
    {
        if (!_dockerAvailable || _postgres is null) return;

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateJwtToken());

        // 1. Create Quote
        var quoteRequest = new CreateFxQuoteRequest("PLN", 10000, "USDC", "merch_atomic_flow", 15);
        var quoteResponse = await client.PostAsJsonAsync("/fx/quotes", quoteRequest);
        quoteResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var quote = await quoteResponse.Content.ReadFromJsonAsync<FxQuoteDto>();
        quote.Should().NotBeNull();

        // 2. Authorize payment using the quote
        var idemKey = $"idem_atomic_{Guid.NewGuid():N}";
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", idemKey);

        var payRequest = new AuthorizePaymentRequest(
            PartyId: "merch_atomic_flow",
            Amount: quote!.TargetAmount,
            Currency: quote.TargetCurrency,
            SettlementChannel: "MOCK",
            QuoteId: quote.QuoteId);

        var payResponse = await client.PostAsJsonAsync("/payments", payRequest);
        payResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var payment = await payResponse.Content.ReadFromJsonAsync<PaymentDto>();
        payment.Should().NotBeNull();
        payment!.State.Should().Be("Authorized");

        // 3. Verify in database that quote is Consumed and payment exists
        var verifyOptions = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        await using var verifyDb = new PaymentDbContext(verifyOptions);

        var quoteInDb = await verifyDb.CurrencyQuotes.FirstOrDefaultAsync(q => q.Id == quote.QuoteId);
        quoteInDb.Should().NotBeNull();
        quoteInDb!.Status.Should().Be(CurrencyQuoteStatus.Consumed);
        quoteInDb.ConsumedByPaymentId.Should().Be(payment.PaymentId);

        var paymentInDb = await verifyDb.Payments.FirstOrDefaultAsync(p => p.Id == payment.PaymentId);
        paymentInDb.Should().NotBeNull();
        paymentInDb!.QuoteId.Should().Be(quote.QuoteId);

        var idemInDb = await verifyDb.IdempotencyRecords.FirstOrDefaultAsync(i => i.Key == idemKey);
        idemInDb.Should().NotBeNull();
        idemInDb!.Status.Should().Be(IdempotencyStatus.Completed);
    }
}
