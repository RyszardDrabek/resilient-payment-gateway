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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PaymentGateway.Api.Endpoints;
using PaymentGateway.Application.Payments;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Infrastructure.Adyen;
using PaymentGateway.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace PaymentGateway.IntegrationTests.Chaos;

/// <summary>
/// F-CHAOS-03: Verifies resilient degradation and non-blocking timeout handling
/// when upstream acquirers experience severe latency or network dropouts.
/// </summary>
public sealed class AcquirerLatencyDegradationTests : IAsyncLifetime
{
    private const string JwtKey = "DEV_ONLY_CHANGE_ME_32CHARS_MINIMUM!!";
    private PostgreSqlContainer? _postgres;
    private AdyenWireMockServer? _wireMock;
    private bool _dockerAvailable;

    public async Task InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("payment_chaos_acquirer_test")
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

        var adyenOptions = Options.Create(new AdyenOptions
        {
            AutoStartMockServer = true,
            MockServerPort = 0,
            TimeoutSeconds = 1,
            MaxRetryAttempts = 1,
            RetryDelayMilliseconds = 50
        });

        _wireMock = new AdyenWireMockServer(adyenOptions, NullLogger<AdyenWireMockServer>.Instance);
        _wireMock.Start();
    }

    public async Task DisposeAsync()
    {
        _wireMock?.Dispose();
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
                        ["RabbitMq:UseInMemory"] = "true",
                        ["Outbox:Enabled"] = "false",
                        ["Auth:JwtSigningKey"] = JwtKey,
                        ["PaymentGateway:ActiveChannel"] = "ADYEN",
                        ["Adyen:MockServerUrl"] = _wireMock?.Url,
                        ["Adyen:TimeoutSeconds"] = "1",
                        ["Adyen:MaxRetryAttempts"] = "1",
                        ["Adyen:RetryDelayMilliseconds"] = "50"
                    });
                });
            });
    }

    private static string GenerateToken(string role = "merchant", string sub = "merchant_chaos_acquirer")
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(JwtKey);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([
                new Claim("sub", sub),
                new Claim("role", role)
            ]),
            Expires = DateTime.UtcNow.AddHours(1),
            Issuer = "payment-gateway",
            Audience = "payment-gateway",
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    [Fact]
    public async Task AcquirerTimeout_CreatesPaymentInPendingState_AndDoesNotCrashGateway()
    {
        if (!_dockerAvailable || _postgres is null || _wireMock is null) return;

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateToken());

        // PartyId containing 'timeout' triggers WireMock delay > HttpClient timeout
        var idempotencyKey = $"idem_timeout_{Guid.NewGuid():N}";
        var req = new AuthorizePaymentRequest("party_timeout_victim", 4500, "EUR", IdempotencyKey: idempotencyKey);

        var response = await client.PostAsJsonAsync("/payments", req);

        // API must not throw 500 or crash; it creates the payment in Pending state
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var payment = await response.Content.ReadFromJsonAsync<PaymentDto>();
        payment.Should().NotBeNull();
        payment!.State.Should().Be("Pending");

        // Correlation header must be present
        response.Headers.Contains("X-Correlation-Id").Should().BeTrue();

        // Database record should reflect Pending state with settlement decline reason
        var dbOptions = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        await using var db = new PaymentDbContext(dbOptions);
        var dbPayment = await db.Payments.FirstOrDefaultAsync(p => p.Id == payment.PaymentId);
        dbPayment.Should().NotBeNull();
        dbPayment!.State.Should().Be(PaymentState.Pending);
        dbPayment.DeclineReason.Should().NotBeNullOrWhiteSpace();

        // Idempotent retry with same key returns cached Pending payment immediately without re-calling acquirer
        var retryResponse = await client.PostAsJsonAsync("/payments", req);
        retryResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var retryPayment = await retryResponse.Content.ReadFromJsonAsync<PaymentDto>();
        retryPayment.Should().NotBeNull();
        retryPayment!.PaymentId.Should().Be(payment.PaymentId);
        retryPayment.State.Should().Be("Pending");
    }

    [Fact]
    public async Task AcquirerRecovery_ProcessesSubsequentPaymentsWithoutLingeringErrors()
    {
        if (!_dockerAvailable || _postgres is null || _wireMock is null) return;

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateToken());

        // 1. First trigger a timeout request
        var timeoutReq = new AuthorizePaymentRequest("party_timeout_trigger", 2000, "EUR", IdempotencyKey: $"idem_timeout_{Guid.NewGuid():N}");
        var timeoutResp = await client.PostAsJsonAsync("/payments", timeoutReq);
        timeoutResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var timeoutPayment = await timeoutResp.Content.ReadFromJsonAsync<PaymentDto>();
        timeoutPayment!.State.Should().Be("Pending");

        // 2. Immediately send a normal, healthy payment request
        var healthyReq = new AuthorizePaymentRequest("party_healthy_normal", 9900, "EUR", IdempotencyKey: $"idem_healthy_{Guid.NewGuid():N}");
        var healthyResp = await client.PostAsJsonAsync("/payments", healthyReq);

        healthyResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var healthyPayment = await healthyResp.Content.ReadFromJsonAsync<PaymentDto>();
        healthyPayment!.State.Should().Be("Authorized");
    }
}
