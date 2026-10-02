using System.Collections.Generic;
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
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using PaymentGateway.Api.Endpoints;
using PaymentGateway.Application.Events;
using PaymentGateway.Application.Payments;
using PaymentGateway.Infrastructure.Events;
using PaymentGateway.Infrastructure.Persistence;
using PaymentGateway.Infrastructure.Services;
using Testcontainers.PostgreSql;
using Xunit;

namespace PaymentGateway.IntegrationTests;

public sealed class PaymentLifecycleEventPublicationTests : IAsyncLifetime
{
    private const string JwtKey = "DEV_ONLY_CHANGE_ME_32CHARS_MINIMUM!!";
    private PostgreSqlContainer? _postgres;
    private bool _dockerAvailable;

    public async Task InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("payment_evt_test")
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
                builder.UseSetting("ConnectionStrings:PaymentDb", connectionString);
                builder.UseSetting("ConnectionStrings:RiskDb", connectionString);
                builder.UseSetting("RabbitMq:UseInMemory", "true");
                builder.UseSetting("Auth:JwtSigningKey", JwtKey);
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

    private static string GenerateTestToken(string role = "merchant", string sub = "test-merchant")
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(JwtKey);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, sub),
                new Claim("sub", sub),
                new Claim(ClaimTypes.Role, role),
                new Claim("role", role)
            }),
            Expires = DateTime.UtcNow.AddHours(1),
            Issuer = "payment-gateway",
            Audience = "payment-gateway",
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    [Fact]
    public async Task AuthorizePayment_PublishesLifecycleEvent_DeliveredToTestConsumer()
    {
        if (!_dockerAvailable || _postgres is null)
        {
            return;
        }

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateTestToken());
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"key_evt_auth_{Guid.NewGuid():N}");

        var eventStore = factory.Services.GetRequiredService<ITestEventStore>();
        eventStore.Clear();

        var request = new AuthorizePaymentRequest("party_evt_100", 2500, "EUR");
        var response = await client.PostAsJsonAsync("/payments", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        // The background OutboxDispatcherService polls every 100 ms — wait for it to dispatch and the bus consumer to record.
        // Polling avoids a race condition where a manual call and the running background service both pick up the same row.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        IReadOnlyList<PaymentLifecycleEvent> events = [];
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
            events = eventStore.GetEvents();
            if (events.Count > 0) break;
        }

        events.Should().ContainSingle();

        var lifecycleEvent = events[0];
        lifecycleEvent.PartyId.Should().Be("party_evt_100");
        lifecycleEvent.Outcome.Should().Be("authorized");
        lifecycleEvent.Amount.Should().Be(2500);
        lifecycleEvent.Currency.Should().Be("EUR");
        lifecycleEvent.Version.Should().Be(1);
        lifecycleEvent.EventId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ProcessCrashAfterCommit_DeliversEventOnRestart()
    {
        if (!_dockerAvailable || _postgres is null)
        {
            return;
        }

        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        // 1. Start a fresh host instance (restart) — CreateClient() starts the host and the MassTransit bus
        await using var factory = CreateFactory();
        var _ = factory.CreateClient(); // starts the host so the in-memory bus and consumers are live
        var eventStore = factory.Services.GetRequiredService<ITestEventStore>();
        eventStore.Clear();

        // 2. Seed payment + outbox record AFTER clearing the event store to avoid the race where the
        //    background service processes the record before Clear() is called.
        var payment = PaymentGateway.Domain.Entities.Payment.Authorize(
            "party_kill_test",
            4000L,
            "EUR",
            "MOCK",
            "mock_tx_kill");

        var eventId = $"evt_crash_{Guid.NewGuid():N}";
        var lifecycleEvent = new PaymentLifecycleEvent(
            EventId: eventId,
            PaymentId: payment.Id,
            PartyId: payment.PartyId,
            Outcome: "authorized",
            Amount: payment.Amount,
            Currency: payment.Currency,
            Version: payment.Version,
            OccurredAt: DateTimeOffset.UtcNow);

        await using (var db = new PaymentDbContext(options))
        {
            db.Payments.Add(payment);
            db.OutboxMessages.Add(new OutboxMessageRecord
            {
                Id = Guid.NewGuid(),
                EventType = PaymentLifecycleEvent.EventType,
                Payload = System.Text.Json.JsonSerializer.Serialize(lifecycleEvent),
                CreatedAt = DateTimeOffset.UtcNow,
                ProcessedAt = null // Unprocessed — simulates process death right after commit
            });
            await db.SaveChangesAsync();
        }

        // 3. Poll until the background OutboxDispatcherService picks up and dispatches the record (up to 5 s)
        var deadline = DateTime.UtcNow.AddSeconds(5);
        IReadOnlyList<PaymentLifecycleEvent> received = [];
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
            received = eventStore.GetEvents();
            if (received.Any(e => e.EventId == eventId)) break;
        }

        received.Should().Contain(e => e.EventId == eventId && e.PartyId == "party_kill_test");
    }
}
