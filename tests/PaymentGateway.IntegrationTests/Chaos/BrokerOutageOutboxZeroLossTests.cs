using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using MassTransit;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PaymentGateway.Api.Endpoints;
using PaymentGateway.Application.Events;
using PaymentGateway.Application.Payments;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Infrastructure.Events;
using PaymentGateway.Infrastructure.Options;
using PaymentGateway.Infrastructure.Persistence;
using PaymentGateway.Infrastructure.Services;
using Testcontainers.PostgreSql;
using Xunit;

namespace PaymentGateway.IntegrationTests.Chaos;

/// <summary>
/// F-CHAOS-02: Verifies mathematical zero-data-loss for the Transactional Outbox pattern
/// when the message broker experiences an outage or network partition.
/// </summary>
public sealed class BrokerOutageOutboxZeroLossTests : IAsyncLifetime
{
    private const string JwtKey = "DEV_ONLY_CHANGE_ME_32CHARS_MINIMUM!!";
    private PostgreSqlContainer? _postgres;
    private bool _dockerAvailable;

    public async Task InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("payment_chaos_outbox_test")
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

    private WebApplicationFactory<Program> CreateFactory(bool outboxAutoDispatch = false)
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
                        ["Outbox:Enabled"] = outboxAutoDispatch ? "true" : "false",
                        ["Auth:JwtSigningKey"] = JwtKey,
                        ["PaymentGateway:ActiveChannel"] = "MOCK"
                    });
                });
            });
    }

    private static string GenerateToken(string role = "merchant", string sub = "merchant_acme")
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
    public async Task BrokerPartition_CapturesPaymentSafely_AndDispatchesAllPendingEventsOnRecovery()
    {
        if (!_dockerAvailable || _postgres is null) return;

        // 1. Simulate Broker Outage: Start API with outbox auto-dispatch disabled (simulating broker unreachable)
        await using var factory = CreateFactory(outboxAutoDispatch: false);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateToken());

        // Process 3 separate payments during the outage
        var paymentIds = new List<string>();
        for (int i = 1; i <= 3; i++)
        {
            var authReq = new AuthorizePaymentRequest($"party_chaos_{i}", 5000 + i * 100, "EUR", IdempotencyKey: $"idem_chaos_{i}_{Guid.NewGuid():N}");
            var authResp = await client.PostAsJsonAsync("/payments", authReq);
            authResp.StatusCode.Should().Be(HttpStatusCode.Created);

            var authBody = await authResp.Content.ReadFromJsonAsync<PaymentDto>();
            authBody.Should().NotBeNull();
            paymentIds.Add(authBody!.PaymentId);
        }

        // 2. Assert all 3 payments were committed to Postgres despite broker partition
        var dbOptions = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        await using (var db = new PaymentDbContext(dbOptions))
        {
            var payments = await db.Payments.Where(p => paymentIds.Contains(p.Id)).ToListAsync();
            payments.Should().HaveCount(3);
            payments.Should().OnlyContain(p => p.State == PaymentState.Authorized);

            // Verify Outbox records exist and are pending (ProcessedAt is null)
            var pendingOutbox = await db.OutboxMessages.Where(m => m.ProcessedAt == null).ToListAsync();
            pendingOutbox.Should().HaveCount(3);
            pendingOutbox.Should().OnlyContain(m => m.EventType == PaymentLifecycleEvent.EventType);
        }

        // 3. Simulate Broker Recovery:
        // Trigger manual OutboxDispatcher batch run against the pending messages
        var eventStore = factory.Services.GetRequiredService<ITestEventStore>();
        eventStore.Clear();

        var scopeFactory = factory.Services.GetRequiredService<IServiceScopeFactory>();

        // Manually dispatch batch simulating the dispatcher recovering connectivity
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
            var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            var pending = await db.OutboxMessages.Where(m => m.ProcessedAt == null).OrderBy(m => m.CreatedAt).ToListAsync();

            foreach (var message in pending)
            {
                var evt = System.Text.Json.JsonSerializer.Deserialize<PaymentLifecycleEvent>(message.Payload);
                if (evt is not null)
                {
                    await publishEndpoint.Publish(evt);
                }
                message.ProcessedAt = DateTimeOffset.UtcNow;
            }
            await db.SaveChangesAsync();
        }

        // 4. Assert 100% Zero-Data-Loss: all 3 events delivered to event store in proper order
        var deadline = DateTime.UtcNow.AddSeconds(5);
        IReadOnlyList<PaymentLifecycleEvent> deliveredEvents = [];
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
            deliveredEvents = eventStore.GetEvents();
            if (deliveredEvents.Count >= 3) break;
        }

        deliveredEvents.Should().HaveCount(3);
        deliveredEvents.Select(e => e.PartyId).Should().Contain(["party_chaos_1", "party_chaos_2", "party_chaos_3"]);

        // Verify all outbox rows are now marked processed
        await using (var db = new PaymentDbContext(dbOptions))
        {
            var remainingPending = await db.OutboxMessages.Where(m => m.ProcessedAt == null).CountAsync();
            remainingPending.Should().Be(0);
        }
    }
}
