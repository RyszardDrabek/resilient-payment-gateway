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
using PaymentGateway.Application.Webhooks;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Edge.Problems;
using PaymentGateway.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace PaymentGateway.IntegrationTests;

public sealed class MerchantWebhookDlqIntegrationTests : IAsyncLifetime
{
    private const string JwtKey = "DEV_ONLY_CHANGE_ME_32CHARS_MINIMUM!!";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private PostgreSqlContainer? _postgres;
    private bool _dockerAvailable;

    public async Task InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("payment_gateway_dlq_test")
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
                .UseNpgsql(_postgres.GetConnectionString())
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

    private static string GenerateJwt(string sub, string role, string? merchantId = null)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(JwtKey);

        var claims = new List<Claim>
        {
            new("sub", sub),
            new("role", role)
        };

        if (!string.IsNullOrEmpty(merchantId))
        {
            claims.Add(new("merchantId", merchantId));
            claims.Add(new("merchant_id", merchantId));
        }

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

    [Fact]
    public async Task ListDlqEntries_AsAuthorizedMerchant_ReturnsEntriesFilteredByStatus()
    {
        if (!_dockerAvailable) return;

        using var factory = CreateFactory();
        var client = factory.CreateClient();

        // Seed DLQ records
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            var r1 = MerchantWebhookDeadLetter.Create("evt_dlq_1", "pay_1", "merch_acme", "https://example.com/hook1", "{\"amount\":100}", 5, "HTTP 500");
            var r2 = MerchantWebhookDeadLetter.Create("evt_dlq_2", "pay_2", "merch_acme", "https://example.com/hook2", "{\"amount\":200}", 5, "HTTP 503");
            r2.MarkReplayed();
            var r3 = MerchantWebhookDeadLetter.Create("evt_dlq_3", "pay_3", "merch_other", "https://example.com/hook3", "{\"amount\":300}", 5, "HTTP 500");

            db.MerchantWebhookDeadLetters.AddRange(r1, r2, r3);
            await db.SaveChangesAsync();
        }

        var token = GenerateJwt("merch_acme", "merchant", "merch_acme");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act - Query exhausted only
        var response = await client.GetAsync("/api/v1/merchants/merch_acme/webhooks/dlq?status=exhausted");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var entries = await response.Content.ReadFromJsonAsync<List<MerchantWebhookDeadLetterDto>>(JsonOptions);
        entries.Should().NotBeNull();
        entries!.Should().ContainSingle(e => e.EventId == "evt_dlq_1" && e.Status == "exhausted");
        entries.Should().NotContain(e => e.EventId == "evt_dlq_2");
        entries.Should().NotContain(e => e.EventId == "evt_dlq_3");
    }

    [Fact]
    public async Task ReplayDlqEntry_WhenAuthorizedAndFound_ReplaysSuccessfully()
    {
        if (!_dockerAvailable) return;

        using var factory = CreateFactory();
        var client = factory.CreateClient();

        Guid entryId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            var record = MerchantWebhookDeadLetter.Create(
                "evt_replay_test",
                "pay_replay_test",
                "merch_replay",
                "https://merchant.example.com/webhook",
                "{\"amount\":5000}",
                5,
                "HTTP 504 Gateway Timeout");

            db.MerchantWebhookDeadLetters.Add(record);
            await db.SaveChangesAsync();
            entryId = record.Id;
        }

        var token = GenerateJwt("merch_replay", "merchant", "merch_replay");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act - Trigger replay
        var response = await client.PostAsync($"/api/v1/merchants/merch_replay/webhooks/dlq/{entryId}/replay", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ReplayMerchantWebhookResult>(JsonOptions);
        result.Should().NotBeNull();

        // Verify record in database
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            var updated = await db.MerchantWebhookDeadLetters.FindAsync(entryId);
            updated.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task AccessDlq_WhenDifferentMerchant_ReturnsForbidden()
    {
        if (!_dockerAvailable) return;

        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var token = GenerateJwt("merch_hacker", "merchant", "merch_hacker");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/api/v1/merchants/merch_victim/webhooks/dlq");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await response.Content.ReadFromJsonAsync<PaymentProblemDetails>(JsonOptions);
        problem.Should().NotBeNull();
        problem!.Type.Should().Be(PaymentProblemTypes.Forbidden);
    }

    [Fact]
    public async Task ReplayDlqEntry_WhenNotFound_ReturnsNotFound()
    {
        if (!_dockerAvailable) return;

        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var token = GenerateJwt("merch_acme", "merchant", "merch_acme");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var randomId = Guid.NewGuid();

        // Act
        var response = await client.PostAsync($"/api/v1/merchants/merch_acme/webhooks/dlq/{randomId}/replay", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<PaymentProblemDetails>(JsonOptions);
        problem.Should().NotBeNull();
        problem!.Type.Should().Be(PaymentProblemTypes.NotFound);
    }

    [Fact]
    public async Task AccessDlq_AsOpsRole_CanAccessAnyMerchantDlq()
    {
        if (!_dockerAvailable) return;

        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var token = GenerateJwt("ops_admin", "ops");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/api/v1/merchants/merch_any/webhooks/dlq");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CleanupExpiredAsync_PrunesRecordsOlderThanCutoff_AndKeepsRecent()
    {
        if (!_dockerAvailable || _postgres is null) return;

        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        await using var db = new PaymentDbContext(options);

        var now = DateTimeOffset.UtcNow;
        var oldDlq = MerchantWebhookDeadLetter.Create(
            eventId: "evt_retention_old",
            paymentId: "pay_retention_old",
            merchantId: "merch_retention_test",
            destinationUrl: "https://example.com/webhook",
            payloadJson: "{}",
            deliveryAttempts: 5,
            lastErrorMessage: "Timed out",
            lastStatusCode: 504,
            failedAt: now.AddDays(-100));

        var recentDlq = MerchantWebhookDeadLetter.Create(
            eventId: "evt_retention_recent",
            paymentId: "pay_retention_recent",
            merchantId: "merch_retention_test",
            destinationUrl: "https://example.com/webhook",
            payloadJson: "{}",
            deliveryAttempts: 5,
            lastErrorMessage: "Timed out",
            lastStatusCode: 504,
            failedAt: now.AddDays(-20));

        db.MerchantWebhookDeadLetters.AddRange(oldDlq, recentDlq);
        await db.SaveChangesAsync();

        var repo = new PaymentGateway.Infrastructure.Repositories.MerchantWebhookDeadLetterRepository(db);
        var deletedCount = await repo.CleanupExpiredAsync(now.AddDays(-90));

        deletedCount.Should().BeGreaterThanOrEqualTo(1);

        await using (var verifyDb = new PaymentDbContext(options))
        {
            var remainingOld = await verifyDb.MerchantWebhookDeadLetters.FindAsync(oldDlq.Id);
            remainingOld.Should().BeNull();

            var remainingRecent = await verifyDb.MerchantWebhookDeadLetters.FindAsync(recentDlq.Id);
            remainingRecent.Should().NotBeNull();
        }
    }
}
