using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using PaymentGateway.Edge.Problems;
using PaymentGateway.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace PaymentGateway.IntegrationTests;

public sealed class SsoAndRbacIntegrationTests : IAsyncLifetime
{
    private const string JwtKey = "DEV_ONLY_CHANGE_ME_32CHARS_MINIMUM!!";
    private PostgreSqlContainer? _postgres;
    private bool _dockerAvailable;

    public async Task InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("payment_gateway_sso_test")
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
        if (_postgres is not null)
        {
            await _postgres.DisposeAsync().AsTask();
        }
    }

    private WebApplicationFactory<Program> CreateFactory(Action<Dictionary<string, string?>>? configureOverrides = null)
    {
        var connectionString = _dockerAvailable && _postgres is not null
            ? _postgres.GetConnectionString()
            : "Host=localhost;Database=dummy;Username=dummy;Password=dummy";

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:PaymentDb"] = connectionString,
            ["ConnectionStrings:RiskDb"] = connectionString,
            ["RabbitMq:UseInMemory"] = "true",
            ["Auth:JwtSigningKey"] = JwtKey,
            ["PaymentGateway:ActiveChannel"] = "MOCK"
        };

        configureOverrides?.Invoke(settings);

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(settings);
                });
            });
    }

    private static string GenerateToken(IEnumerable<Claim> claims)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(JwtKey);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
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

    private static string GenerateRoleToken(string role, string subject = "test_user") =>
        GenerateToken([new Claim("sub", subject), new Claim("role", role)]);

    [Fact]
    public async Task AnonymousRequest_ToProtectedEndpoint_Returns401WithProblemDetails()
    {
        if (!_dockerAvailable) return;

        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/payments/unresolved");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.Contains("X-Correlation-Id").Should().BeTrue();
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<PaymentProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Status.Should().Be(401);
        problem.Type.Should().Be(PaymentProblemTypes.Unauthorized);
    }

    [Fact]
    public async Task MerchantRole_AccessingRestrictedRiskEndpoint_Returns403WithProblemDetails()
    {
        if (!_dockerAvailable) return;

        using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateRoleToken("merchant"));

        var response = await client.GetAsync("/api/v1/risk/flags");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Headers.Contains("X-Correlation-Id").Should().BeTrue();
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<PaymentProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Status.Should().Be(403);
        problem.Type.Should().Be(PaymentProblemTypes.Forbidden);
    }

    [Fact]
    public async Task OpsRole_AccessingUnresolvedPayments_Returns200Ok()
    {
        if (!_dockerAvailable) return;

        using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateRoleToken("ops"));

        var response = await client.GetAsync("/api/v1/payments/unresolved");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task KeycloakRealmAccessClaim_WithRiskRole_AuthorizesAccessToRiskEndpoint()
    {
        if (!_dockerAvailable) return;

        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var token = GenerateToken([
            new Claim("sub", "keycloak_auditor"),
            new Claim("preferred_username", "compliance_user"),
            new Claim("realm_access", JsonSerializer.Serialize(new { roles = new[] { "risk", "offline_access" } }))
        ]);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/risk/flags");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DualModeConfiguration_WithAuthorityAndSigningKey_AcceptsSymmetricDevToken()
    {
        if (!_dockerAvailable) return;

        using var factory = CreateFactory(cfg =>
        {
            cfg["Auth:Authority"] = "http://keycloak.internal:8080/realms/payment-gateway";
            cfg["Auth:RequireHttpsMetadata"] = "false";
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateRoleToken("ops"));

        var response = await client.GetAsync("/api/v1/payments/unresolved");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
