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
using PaymentGateway.Application.Events;
using PaymentGateway.Application.Payments;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Edge.Problems;
using PaymentGateway.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace PaymentGateway.IntegrationTests;

public sealed class Phase5OperationalHardeningEndToEndTests : IAsyncLifetime
{
    private const string JwtKey = "DEV_ONLY_CHANGE_ME_32CHARS_MINIMUM!!";
    private PostgreSqlContainer? _postgres;
    private bool _dockerAvailable;

    public async Task InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("payment_phase5_e2e_test")
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

    private static string GenerateToken(string role)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(JwtKey);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([
                new Claim("sub", $"user_{role}"),
                new Claim("role", role)
            ]),
            Expires = DateTime.UtcNow.AddHours(1),
            Issuer = "payment-gateway",
            Audience = "payment-gateway",
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    [Fact]
    public async Task E2E_Phase5_PartialCaptureAndPartialRefund_MultiStepJourney_MaintainsBalanceInvariant()
    {
        if (!_dockerAvailable || _postgres is null) return;

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateToken("merchant"));

        // Step 1: Authorize payment of 100.00 EUR (10000 cents)
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"idem_e2e_auth_{Guid.NewGuid():N}");
        var authResponse = await client.PostAsJsonAsync("/payments", new AuthorizePaymentRequest("party_e2e_phase5", 10000, "EUR"));
        authResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var authDto = await authResponse.Content.ReadFromJsonAsync<PaymentDto>();
        authDto.Should().NotBeNull();
        authDto!.State.Should().Be("Authorized");
        authDto.RemainingAuthorizedAmount.Should().Be(10000);
        authDto.CapturedAmount.Should().Be(0);

        var paymentId = authDto.PaymentId;

        // Step 2: Partial capture of 40.00 EUR (4000 cents) -> PartiallyCaptured
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"idem_e2e_cap1_{Guid.NewGuid():N}");
        var cap1Response = await client.PostAsJsonAsync($"/payments/{paymentId}/capture", new CapturePaymentRequest(4000));
        cap1Response.StatusCode.Should().Be(HttpStatusCode.OK);
        var cap1Dto = await cap1Response.Content.ReadFromJsonAsync<PaymentDto>();
        cap1Dto.Should().NotBeNull();
        cap1Dto!.State.Should().Be("PartiallyCaptured");
        cap1Dto.CapturedAmount.Should().Be(4000);
        cap1Dto.RemainingAuthorizedAmount.Should().Be(6000);
        cap1Dto.RemainingRefundableAmount.Should().Be(4000);

        // Step 3: Second partial capture of remainder 60.00 EUR (6000 cents) -> Captured
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"idem_e2e_cap2_{Guid.NewGuid():N}");
        var cap2Response = await client.PostAsJsonAsync($"/payments/{paymentId}/capture", new CapturePaymentRequest(6000));
        cap2Response.StatusCode.Should().Be(HttpStatusCode.OK);
        var cap2Dto = await cap2Response.Content.ReadFromJsonAsync<PaymentDto>();
        cap2Dto.Should().NotBeNull();
        cap2Dto!.State.Should().Be("Captured");
        cap2Dto.CapturedAmount.Should().Be(10000);
        cap2Dto.RemainingAuthorizedAmount.Should().Be(0);
        cap2Dto.RemainingRefundableAmount.Should().Be(10000);

        // Step 4: Partial refund of 35.00 EUR (3500 cents) -> PartiallyRefunded
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"idem_e2e_ref1_{Guid.NewGuid():N}");
        var ref1Response = await client.PostAsJsonAsync($"/payments/{paymentId}/refund", new RefundPaymentRequest(3500));
        ref1Response.StatusCode.Should().Be(HttpStatusCode.OK);
        var ref1Dto = await ref1Response.Content.ReadFromJsonAsync<PaymentDto>();
        ref1Dto.Should().NotBeNull();
        ref1Dto!.State.Should().Be("PartiallyRefunded");
        ref1Dto.RefundedAmount.Should().Be(3500);
        ref1Dto.RemainingRefundableAmount.Should().Be(6500);

        // Step 5: Complete refund of remaining 65.00 EUR (6500 cents) -> Refunded
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"idem_e2e_ref2_{Guid.NewGuid():N}");
        var ref2Response = await client.PostAsJsonAsync($"/payments/{paymentId}/refund", new RefundPaymentRequest(6500));
        ref2Response.StatusCode.Should().Be(HttpStatusCode.OK);
        var ref2Dto = await ref2Response.Content.ReadFromJsonAsync<PaymentDto>();
        ref2Dto.Should().NotBeNull();
        ref2Dto!.State.Should().Be("Refunded");
        ref2Dto.RefundedAmount.Should().Be(10000);
        ref2Dto.RemainingRefundableAmount.Should().Be(0);
    }

    [Fact]
    public async Task E2E_Phase5_UnresolvedPaymentsQuery_SecurityAndFiltering_Flow()
    {
        if (!_dockerAvailable || _postgres is null) return;

        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        // 1. Unauthenticated request to /api/v1/payments/unresolved returns 401 Problem Details
        var unauthResponse = await client.GetAsync("/api/v1/payments/unresolved");
        unauthResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        unauthResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var unauthBody = await unauthResponse.Content.ReadAsStringAsync();
        using (var doc = JsonDocument.Parse(unauthBody))
        {
            doc.RootElement.GetProperty("type").GetString().Should().Be(PaymentProblemTypes.Unauthorized);
        }

        // 2. Unauthorized role request returns 403 Problem Details
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateToken("invalid_role"));
        var forbiddenResponse = await client.GetAsync("/api/v1/payments/unresolved");
        forbiddenResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        forbiddenResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var forbiddenBody = await forbiddenResponse.Content.ReadAsStringAsync();
        using (var doc = JsonDocument.Parse(forbiddenBody))
        {
            doc.RootElement.GetProperty("type").GetString().Should().Be(PaymentProblemTypes.Forbidden);
        }

        // 3. Authorized operator request returns 200 OK paginated envelope
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateToken("ops"));
        var opsResponse = await client.GetAsync("/api/v1/payments/unresolved?ageMinutes=0&pageSize=10");
        opsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await opsResponse.Content.ReadFromJsonAsync<UnresolvedPaymentsResponse>();
        envelope.Should().NotBeNull();
        envelope!.Page.Should().Be(1);
        envelope.PageSize.Should().Be(10);
        envelope.Items.Should().NotBeNull();
    }

    [Fact]
    public async Task E2E_Phase5_OutboxRetention_PruningCycle_CleansExpiredMessages()
    {
        if (!_dockerAvailable || _postgres is null) return;

        await using var factory = CreateFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var retentionService = scope.ServiceProvider.GetRequiredService<IOutboxRetentionService>();

        // Seed expired processed message (older than 14 days) and active/unprocessed message
        var expiredMessage = new OutboxMessageRecord
        {
            Id = Guid.NewGuid(),
            EventType = "PaymentCompletedEvent",
            Payload = "{\"test\":true}",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-20),
            ProcessedAt = DateTimeOffset.UtcNow.AddDays(-19),
            DeliveryAttempts = 1
        };

        var recentMessage = new OutboxMessageRecord
        {
            Id = Guid.NewGuid(),
            EventType = "PaymentCompletedEvent",
            Payload = "{\"test\":true}",
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            ProcessedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
            DeliveryAttempts = 1
        };

        db.OutboxMessages.AddRange(expiredMessage, recentMessage);
        await db.SaveChangesAsync();

        // Run retention cleanup
        var deletedCount = await retentionService.CleanupExpiredOutboxMessagesAsync();
        deletedCount.Should().BeGreaterThanOrEqualTo(1);

        // Verify expired message was pruned and recent message remains
        db.ChangeTracker.Clear();
        var remainingExpired = await db.OutboxMessages.FindAsync(expiredMessage.Id);
        remainingExpired.Should().BeNull();

        var remainingRecent = await db.OutboxMessages.FindAsync(recentMessage.Id);
        remainingRecent.Should().NotBeNull();
    }
}
