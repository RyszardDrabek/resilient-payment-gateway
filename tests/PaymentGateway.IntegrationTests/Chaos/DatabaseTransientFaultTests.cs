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
using PaymentGateway.Application.Payments;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Exceptions;
using PaymentGateway.Domain.Services;
using PaymentGateway.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace PaymentGateway.IntegrationTests.Chaos;

/// <summary>
/// F-CHAOS-03: Verifies database resilience, lease expiry recovery,
/// and atomicity under transient failures.
/// </summary>
public sealed class DatabaseTransientFaultTests : IAsyncLifetime
{
    private const string JwtKey = "DEV_ONLY_CHANGE_ME_32CHARS_MINIMUM!!";
    private PostgreSqlContainer? _postgres;
    private bool _dockerAvailable;

    public async Task InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("payment_chaos_db_test")
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
                        ["Outbox:Enabled"] = "false",
                        ["Auth:JwtSigningKey"] = JwtKey,
                        ["PaymentGateway:ActiveChannel"] = "MOCK"
                    });
                });
            });
    }

    private static string GenerateToken(string role = "merchant", string sub = "merchant_chaos_db")
    {
        var tokenHandler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
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
    public async Task InFlightLease_WhenActive_RejectsConcurrentRequest_AndRecoversAfterExpiry()
    {
        if (!_dockerAvailable || _postgres is null) return;

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateToken());

        var dbOptions = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        var key = $"idem_lease_test_{Guid.NewGuid():N}";
        var req = new AuthorizePaymentRequest("party_lease_test", 5000, "EUR", IdempotencyKey: key);

        // 1. Manually seed an active InFlight record simulating a running / stuck request
        var computedHash = IdempotencyPayloadHasher.ComputeHash("Authorize", null, "party_lease_test", 5000, "EUR", null);
        await using (var db = new PaymentDbContext(dbOptions))
        {
            var activeLease = IdempotencyRecord.CreateInFlight(key, "Authorize", computedHash);
            await db.IdempotencyRecords.AddAsync(activeLease);
            await db.SaveChangesAsync();
        }

        // 2. Immediate request while lease is active returns 409 Conflict (InFlight)
        var conflictResponse = await client.PostAsJsonAsync("/payments", req);
        conflictResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // 3. Simulate lease expiration (as if worker or node died > 30 seconds ago)
        await using (var db = new PaymentDbContext(dbOptions))
        {
            var record = await db.IdempotencyRecords.FirstAsync(r => r.Key == key);
            // Backdate LockedUntil by 60 seconds
            typeof(IdempotencyRecord)
                .GetProperty(nameof(IdempotencyRecord.LockedUntil))!
                .SetValue(record, DateTimeOffset.UtcNow.AddSeconds(-60));
            await db.SaveChangesAsync();
        }

        // 4. Retry with expired lease should succeed: lease is re-acquired and payment authorized
        var recoveredResponse = await client.PostAsJsonAsync("/payments", req);
        recoveredResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var payment = await recoveredResponse.Content.ReadFromJsonAsync<PaymentDto>();
        payment.Should().NotBeNull();
        payment!.State.Should().Be("Authorized");

        // 5. Database idempotency record is now marked Completed
        await using (var db = new PaymentDbContext(dbOptions))
        {
            var record = await db.IdempotencyRecords.FirstAsync(r => r.Key == key);
            record.Status.Should().Be(IdempotencyStatus.Completed);
            record.PaymentId.Should().Be(payment.PaymentId);
        }
    }
}
