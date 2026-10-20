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
using PaymentGateway.Application.Payments;
using PaymentGateway.Application.Risk.Ports;
using PaymentGateway.Infrastructure.Persistence;
using PaymentGateway.Infrastructure.Risk;
using Testcontainers.PostgreSql;
using Xunit;

namespace PaymentGateway.IntegrationTests;

public sealed class InlineRiskGatingIntegrationTests : IAsyncLifetime
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
                .WithDatabase("payment_inline_risk_test")
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
                    var dict = new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:PaymentDb"] = connectionString,
                        ["ConnectionStrings:RiskDb"] = connectionString,
                        ["Auth:JwtSigningKey"] = JwtKey,
                        ["RabbitMq:UseInMemory"] = "true",
                        ["PaymentGateway:ActiveChannel"] = "MOCK",
                        ["InlineRisk:Enabled"] = "true",
                        ["InlineRisk:TimeoutMilliseconds"] = "100"
                    };
                    config.AddInMemoryCollection(dict);
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
    public async Task PostPayment_WhenBlacklistedParty_BlocksUpfrontAndPersistsRiskFlagAndIsIdempotent()
    {
        if (!_dockerAvailable)
        {
            return;
        }

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateJwtToken("merchant"));

        var idemKey = "idem-inline-fraud-" + Guid.NewGuid();
        var request = new
        {
            partyId = "party_fraud_attacker_99",
            amount = 1500L,
            currency = "EUR",
            settlementChannel = "MOCK"
        };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/payments")
        {
            Content = JsonContent.Create(request)
        };
        httpRequest.Headers.Add("Idempotency-Key", idemKey);

        var response = await client.SendAsync(httpRequest);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var payment = await response.Content.ReadFromJsonAsync<PaymentDto>();
        payment.Should().NotBeNull();
        payment!.State.Should().Be("Declined");
        payment.DeclineReason.Should().StartWith("FRAUD_BLOCKED:");

        // Verify RiskFlag is persisted in PostgreSQL
        using var scope = factory.Services.CreateScope();
        var flagRepo = scope.ServiceProvider.GetRequiredService<IRiskFlagRepository>();
        var flags = await flagRepo.GetByPaymentIdAsync(payment.PaymentId);
        flags.Should().HaveCount(1);
        flags[0].PartyId.Should().Be("party_fraud_attacker_99");
        flags[0].Reason.Should().Contain("Inline risk blocked");

        // Idempotent retry with same Idempotency-Key returns cached 201 Created and same payment
        var retryRequest = new HttpRequestMessage(HttpMethod.Post, "/payments")
        {
            Content = JsonContent.Create(request)
        };
        retryRequest.Headers.Add("Idempotency-Key", idemKey);

        var retryResponse = await client.SendAsync(retryRequest);
        retryResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var cachedPayment = await retryResponse.Content.ReadFromJsonAsync<PaymentDto>();
        cachedPayment.Should().NotBeNull();
        cachedPayment!.PaymentId.Should().Be(payment.PaymentId);
        cachedPayment.State.Should().Be("Declined");
    }

    [Fact]
    public async Task PostPayment_WhenNormalParty_AuthorizesNormallyWithoutRiskFlag()
    {
        if (!_dockerAvailable)
        {
            return;
        }

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateJwtToken("merchant"));

        var idemKey = "idem-inline-ok-" + Guid.NewGuid();
        var request = new
        {
            partyId = "party_legit_shopper_01",
            amount = 2500L,
            currency = "EUR",
            settlementChannel = "MOCK"
        };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/payments")
        {
            Content = JsonContent.Create(request)
        };
        httpRequest.Headers.Add("Idempotency-Key", idemKey);

        var response = await client.SendAsync(httpRequest);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var payment = await response.Content.ReadFromJsonAsync<PaymentDto>();
        payment.Should().NotBeNull();
        payment!.State.Should().Be("Authorized");

        // Verify NO RiskFlag is created
        using var scope = factory.Services.CreateScope();
        var flagRepo = scope.ServiceProvider.GetRequiredService<IRiskFlagRepository>();
        var flags = await flagRepo.GetByPaymentIdAsync(payment.PaymentId);
        flags.Should().BeEmpty();
    }
}
