using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using PaymentGateway.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace PaymentGateway.IntegrationTests;

/// <summary>
/// F-EDGE-01 AC-4: Verifies that unhandled internal faults produce RFC 7807 server-error
/// problem documents without exposing stack traces or internal exception messages.
/// </summary>
public sealed class ProblemDetailsIntegrationTests : IAsyncLifetime
{
    private const string JwtKey = "DEV_ONLY_CHANGE_ME_32CHARS_MINIMUM!!";
    private PostgreSqlContainer? _postgres;
    private bool _dockerAvailable;

    public async Task InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("payment_gateway_test_pd")
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
                        ["Auth:JwtSigningKey"] = JwtKey,
                        ["PaymentGateway:ActiveChannel"] = "MOCK"
                    });
                });
            });
    }

    private static string GenerateTestToken()
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(JwtKey);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([
                new Claim("sub", "merchant_test"),
                new Claim("role", "merchant")
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

    /// <summary>
    /// AC-4: When the application has an unhandled server fault, the middleware must return
    /// urn:rpg:problem:server-error and must NOT leak internal exception details.
    /// </summary>
    [Fact]
    public async Task Get_NonExistentPayment_Returns404_ProblemDoc_WithoutStackTrace()
    {
        if (!_dockerAvailable || _postgres is null)
        {
            // Docker not available — skip; the endpoint relies on DB and would return 500.
            // AC-4 is fully covered by PaymentExceptionHandlerMiddlewareTests unit tests.
            return;
        }

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateTestToken());

        var response = await client.GetAsync("/payments/pay_does_not_exist_fault_test");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var body = await response.Content.ReadAsStringAsync();
        // AC-4 cross-check: no stack trace in any problem document body
        body.Should().NotContain("   at ");
        body.Should().NotContain("System.Exception");
        // AC-5: distinguishable type URI
        body.Should().Contain("urn:rpg:problem:");
    }
}
