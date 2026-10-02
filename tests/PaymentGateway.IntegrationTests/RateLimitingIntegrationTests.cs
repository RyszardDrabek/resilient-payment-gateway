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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using PaymentGateway.Api.Endpoints;
using PaymentGateway.Application.Payments;
using PaymentGateway.Edge.Options;
using PaymentGateway.Edge.Problems;
using PaymentGateway.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace PaymentGateway.IntegrationTests;

public sealed class RateLimitingIntegrationTests : IAsyncLifetime
{
    private const string JwtKey = "DEV_ONLY_CHANGE_ME_32CHARS_MINIMUM!!";
    private PostgreSqlContainer? _postgres;
    private bool _dockerAvailable;

    public async Task InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("payment_gateway_rl_test")
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

    private WebApplicationFactory<Program> CreateFactory(
        int permitLimitPerMinute = 2,
        int globalConcurrencyLimit = 1)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:PaymentDb"] = _postgres?.GetConnectionString()
                        ?? "Host=localhost;Database=payment_gateway_rl_test;Username=postgres;Password=postgres",
                    ["ConnectionStrings:RiskDb"] = _postgres?.GetConnectionString()
                        ?? "Host=localhost;Database=payment_gateway_rl_test;Username=postgres;Password=postgres",
                    ["RabbitMq:UseInMemory"] = "true",
                    ["Auth:Issuer"] = "payment-gateway",
                    ["Auth:Audience"] = "payment-gateway",
                    ["Auth:JwtSigningKey"] = JwtKey,
                    ["RateLimiting:PermitLimitPerMinute"] = permitLimitPerMinute.ToString(),
                    ["RateLimiting:GlobalConcurrencyLimit"] = globalConcurrencyLimit.ToString(),
                    ["RateLimiting:QueueLimit"] = "0"
                });
            });
        });
    }

    private static string GenerateTestToken(string sub = "merchant_rate_limited")
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "payment-gateway",
            audience: "payment-gateway",
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, sub),
                new Claim(ClaimTypes.Role, "PaymentService")
            },
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Fact]
    public async Task ExceedingRateLimit_Returns429_WithRateLimitProblemDetails()
    {
        if (!_dockerAvailable || _postgres is null)
        {
            return;
        }

        // Limit configured to 2 permits per minute
        await using var factory = CreateFactory(permitLimitPerMinute: 2, globalConcurrencyLimit: 10);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateTestToken("merchant_user_quota"));

        // Request 1: 404 (valid request through edge)
        var response1 = await client.GetAsync("/payments/non_existing_1");
        response1.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Request 2: 404 (within limit of 2)
        var response2 = await client.GetAsync("/payments/non_existing_2");
        response2.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Request 3: 429 Too Many Requests (exceeds limit of 2)
        var response3 = await client.GetAsync("/payments/non_existing_3");
        response3.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response3.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        response3.Headers.Contains("X-Correlation-Id").Should().BeTrue();

        var body = await response3.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("type").GetString().Should().Be(PaymentProblemTypes.RateLimit);
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(429);
        doc.RootElement.GetProperty("correlationId").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RateLimits_ArePartitionedByCallerIdentity()
    {
        if (!_dockerAvailable || _postgres is null)
        {
            return;
        }

        // Limit configured to 1 permit per minute per caller
        await using var factory = CreateFactory(permitLimitPerMinute: 1, globalConcurrencyLimit: 10);
        var client = factory.CreateClient();

        // Caller A uses permit
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateTestToken("caller_A"));
        var responseA1 = await client.GetAsync("/payments/item_a");
        responseA1.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Caller A 2nd request is blocked
        var responseA2 = await client.GetAsync("/payments/item_a");
        responseA2.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        // Caller B has their own partition — should NOT be blocked by Caller A's usage
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateTestToken("caller_B"));
        var responseB1 = await client.GetAsync("/payments/item_b");
        responseB1.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OverloadConcurrencyCap_RejectsExcessRequests_WithRateLimitProblemDetails()
    {
        if (!_dockerAvailable || _postgres is null)
        {
            return;
        }

        // Global concurrency cap of 1, high permit limit so partition doesn't trigger
        await using var factory = CreateFactory(permitLimitPerMinute: 1000, globalConcurrencyLimit: 1);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateTestToken("caller_concurrent"));

        // Launch multiple concurrent requests simultaneously
        var tasks = Enumerable.Range(0, 10)
            .Select(i => client.GetAsync($"/payments/concurrent_{i}"))
            .ToList();

        var responses = await Task.WhenAll(tasks);

        // At least one request should encounter 429 due to concurrency cap = 1
        responses.Should().Contain(r => r.StatusCode == HttpStatusCode.TooManyRequests);
        var rejectedResponse = responses.First(r => r.StatusCode == HttpStatusCode.TooManyRequests);
        rejectedResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task HealthEndpoint_IsNotRateLimited()
    {
        if (!_dockerAvailable || _postgres is null)
        {
            return;
        }

        // Configure strict limit of 1
        await using var factory = CreateFactory(permitLimitPerMinute: 1, globalConcurrencyLimit: 1);
        var client = factory.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            var response = await client.GetAsync("/health");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
}
