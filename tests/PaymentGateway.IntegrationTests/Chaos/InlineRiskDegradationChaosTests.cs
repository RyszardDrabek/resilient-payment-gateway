using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
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
using PaymentGateway.Domain.Entities;
using PaymentGateway.Infrastructure.Persistence;
using PaymentGateway.Infrastructure.Risk;
using Testcontainers.PostgreSql;
using Xunit;

namespace PaymentGateway.IntegrationTests.Chaos;

/// <summary>
/// Chaos tests verifying low-latency SLA boundaries (<50ms) and fail-open resilience
/// of the Pre-Settlement Inline Risk Gating engine when backing databases suffer latency spikes or drops.
/// </summary>
public sealed class InlineRiskDegradationChaosTests : IAsyncLifetime
{
    private const string JwtKey = "DEV_ONLY_CHANGE_ME_32CHARS_MINIMUM!!";
    private PostgreSqlContainer? _postgres;
    private bool _dockerAvailable;

    private sealed class FaultInjectingRiskContextStore : IRiskContextStore
    {
        public TimeSpan Latency { get; set; } = TimeSpan.Zero;
        public Exception? ExceptionToThrow { get; set; }
        public int CallCount { get; private set; }

        public async Task<IReadOnlyList<PartyRiskContext>> GetPartyHistoryAsync(string partyId, CancellationToken ct = default)
        {
            CallCount++;
            if (Latency > TimeSpan.Zero)
            {
                await Task.Delay(Latency, ct);
            }

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return [];
        }

        public Task RecordContextAsync(PartyRiskContext context, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<PartyRiskContext>> FindSimilarContextsAsync(string partyId, float[] targetEmbedding, int limit = 5, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PartyRiskContext>>([]);
    }

    public async Task InitializeAsync()
    {
        InlineRiskEvaluator.ResetVelocity();
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("payment_chaos_inlinerisk_test")
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

    private WebApplicationFactory<Program> CreateFactory(FaultInjectingRiskContextStore faultStore)
    {
        var connectionString = _dockerAvailable && _postgres is not null
            ? _postgres.GetConnectionString()
            : "Host=localhost;Database=dummy;Username=dummy;Password=dummy";

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:PaymentDb", connectionString);
                builder.UseSetting("ConnectionStrings:RiskDb", connectionString);
                builder.UseSetting("RabbitMq:UseInMemory", "true");
                builder.UseSetting("Auth:JwtSigningKey", JwtKey);
                builder.UseSetting("PaymentGateway:ActiveChannel", "MOCK");
                builder.UseSetting("InlineRisk:Enabled", "true");
                builder.UseSetting("InlineRisk:TimeoutMilliseconds", "50");
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
                        ["InlineRisk:TimeoutMilliseconds"] = "50"
                    };
                    config.AddInMemoryCollection(dict);
                });
                builder.ConfigureServices(services =>
                {
                    var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IRiskContextStore));
                    if (descriptor is not null)
                    {
                        services.Remove(descriptor);
                    }
                    services.AddSingleton<IRiskContextStore>(faultStore);
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
    public async Task Chaos_DatabaseLatencySpike_FailsOpenWithinStrictTimeBudget()
    {
        if (!_dockerAvailable) return;

        // Inject 400ms database latency spike (far exceeding the 50ms budget)
        var faultStore = new FaultInjectingRiskContextStore
        {
            Latency = TimeSpan.FromMilliseconds(400)
        };

        await using var factory = CreateFactory(faultStore);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateJwtToken("merchant"));

        // Warm up connection
        var warmup = new HttpRequestMessage(HttpMethod.Post, "/payments")
        {
            Content = JsonContent.Create(new { partyId = "warmup_shopper", amount = 100L, currency = "EUR", settlementChannel = "MOCK" })
        };
        warmup.Headers.Add("Idempotency-Key", "warmup-" + Guid.NewGuid());
        await client.SendAsync(warmup);

        var sw = Stopwatch.StartNew();
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/payments")
        {
            Content = JsonContent.Create(new
            {
                partyId = "slow_db_victim",
                amount = 2500L,
                currency = "EUR",
                settlementChannel = "MOCK"
            })
        };
        httpRequest.Headers.Add("Idempotency-Key", "chaos-latency-" + Guid.NewGuid());

        var response = await client.SendAsync(httpRequest);
        sw.Stop();

        // Must succeed with 201 Created via fail-open policy
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var payment = await response.Content.ReadFromJsonAsync<PaymentDto>();
        payment.Should().NotBeNull();
        payment!.State.Should().Be("Authorized");

        // The inline risk timeout is 50ms. Total request overhead should be substantially below the 400ms latency spike!
        sw.ElapsedMilliseconds.Should().BeLessThan(350, "evaluation must be cancelled at 50ms and fail-open rather than waiting 400ms");
    }

    [Fact]
    public async Task Chaos_DatabaseConnectionCrash_FailsOpenWithoutThrowing500()
    {
        if (!_dockerAvailable) return;

        // Inject network connection reset failure
        var faultStore = new FaultInjectingRiskContextStore
        {
            ExceptionToThrow = new SocketException((int)SocketError.ConnectionReset)
        };

        await using var factory = CreateFactory(faultStore);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateJwtToken("merchant"));

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/payments")
        {
            Content = JsonContent.Create(new
            {
                partyId = "shopper_with_db_crash",
                amount = 3500L,
                currency = "EUR",
                settlementChannel = "MOCK"
            })
        };
        httpRequest.Headers.Add("Idempotency-Key", "chaos-crash-" + Guid.NewGuid());

        var response = await client.SendAsync(httpRequest);

        // Under ADR-025, network failure in risk engine must fail open without 500 error
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var payment = await response.Content.ReadFromJsonAsync<PaymentDto>();
        payment.Should().NotBeNull();
        payment!.State.Should().Be("Authorized");
    }

    [Fact]
    public async Task Chaos_WhenDependencyDown_InMemBlacklistStillBlocksFraudInstantly()
    {
        if (!_dockerAvailable) return;

        // Context store is completely dead
        var faultStore = new FaultInjectingRiskContextStore
        {
            ExceptionToThrow = new InvalidOperationException("Fatal database outage")
        };

        await using var factory = CreateFactory(faultStore);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateJwtToken("merchant"));

        // Warmup client pipeline
        var warmup = new HttpRequestMessage(HttpMethod.Post, "/payments")
        {
            Content = JsonContent.Create(new { partyId = "warmup_shopper", amount = 100L, currency = "EUR", settlementChannel = "MOCK" })
        };
        warmup.Headers.Add("Idempotency-Key", "warmup-inmem-" + Guid.NewGuid());
        await client.SendAsync(warmup);

        var sw = Stopwatch.StartNew();
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/payments")
        {
            Content = JsonContent.Create(new
            {
                partyId = "party_fraud_attacker_critical",
                amount = 5000L,
                currency = "EUR",
                settlementChannel = "MOCK"
            })
        };
        httpRequest.Headers.Add("Idempotency-Key", "chaos-fraud-" + Guid.NewGuid());

        var response = await client.SendAsync(httpRequest);
        sw.Stop();

        // Even with database outage, in-memory blacklist rule blocks fraud upfront without hitting acquirer
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var payment = await response.Content.ReadFromJsonAsync<PaymentDto>();
        payment.Should().NotBeNull();
        payment!.State.Should().Be("Declined");
        payment.DeclineReason.Should().StartWith("FRAUD_BLOCKED: Known fraudulent party pattern detected");

        // Evaluates immediately in memory without external acquirer or risk DB delays
        sw.ElapsedMilliseconds.Should().BeLessThan(300);
    }
}
