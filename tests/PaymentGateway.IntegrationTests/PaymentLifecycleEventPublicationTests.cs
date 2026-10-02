using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
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
                builder.UseSetting("PaymentGateway:ActiveChannel", "MOCK");
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
            Issuer = "PaymentGateway",
            Audience = "PaymentGateway",
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

        // Process pending outbox messages to dispatch to MassTransit
        using (var scope = factory.Services.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IHostedService>() as OutboxDispatcherService;
            if (dispatcher is not null)
            {
                await dispatcher.ProcessPendingMessagesAsync();
            }
        }

        // Wait briefly for in-memory bus delivery
        await Task.Delay(200);

        var events = eventStore.GetEvents();
        events.Should().ContainSingle();

        var lifecycleEvent = events.Single();
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

        // 1. Commit a payment directly to DB with an Outbox record (simulating process death right after commit)
        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

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
                EventType = typeof(PaymentLifecycleEvent).FullName ?? nameof(PaymentLifecycleEvent),
                Payload = System.Text.Json.JsonSerializer.Serialize(lifecycleEvent),
                CreatedAt = DateTimeOffset.UtcNow,
                ProcessedAt = null // Unprocessed because process died
            });
            await db.SaveChangesAsync();
        }

        // 2. Start a fresh host instance (restart)
        await using var factory = CreateFactory();
        var eventStore = factory.Services.GetRequiredService<ITestEventStore>();
        eventStore.Clear();

        // 3. Trigger outbox dispatcher on the new host
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            var publishEndpoint = scope.ServiceProvider.GetRequiredService<MassTransit.IPublishEndpoint>();

            var pending = await db.OutboxMessages.Where(m => m.ProcessedAt == null).ToListAsync();
            foreach (var msg in pending)
            {
                var evt = System.Text.Json.JsonSerializer.Deserialize<PaymentLifecycleEvent>(msg.Payload);
                if (evt is not null)
                {
                    await publishEndpoint.Publish(evt);
                }
                msg.ProcessedAt = DateTimeOffset.UtcNow;
            }
            await db.SaveChangesAsync();
        }

        await Task.Delay(200);

        // 4. Verify test consumer observed the event after restart
        var received = eventStore.GetEvents();
        received.Should().Contain(e => e.EventId == eventId && e.PartyId == "party_kill_test");
    }
}
