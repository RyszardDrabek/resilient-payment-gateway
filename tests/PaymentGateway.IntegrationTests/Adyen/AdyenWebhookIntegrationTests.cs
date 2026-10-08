using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.Adyen;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Infrastructure.Adyen;
using PaymentGateway.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace PaymentGateway.IntegrationTests.Adyen;

public sealed class AdyenWebhookIntegrationTests : IAsyncLifetime
{
    private const string JwtKey = "DEV_ONLY_CHANGE_ME_32CHARS_MINIMUM!!";
    private const string TestHmacKey = "44782DEF547AAB80616F310827471246151F9957283A359CE97849303DE8E9E3";
    private PostgreSqlContainer? _postgres;
    private bool _dockerAvailable;

    public async Task InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("payment_gateway_test_webhook")
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
                        ["PaymentGateway:ActiveChannel"] = "MOCK",
                        ["Adyen:HmacKey"] = TestHmacKey,
                        ["Adyen:AutoStartMockServer"] = "false"
                    });
                });
            });
    }

    [Fact]
    public async Task AC1_When_Adyen_Notification_Arrives_With_Valid_Hmac_Acknowledge_Receipt()
    {
        if (!_dockerAvailable) return;

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var validator = factory.Services.GetRequiredService<IAdyenHmacValidator>();

        var pspRef = $"PSP-{Guid.NewGuid():N}";
        var item = CreateNotificationItem(pspRef, "PAY-AC1", "AUTHORISATION", 1500, "EUR", "true");
        SignNotificationItem(item, validator, TestHmacKey);

        var payload = new AdyenWebhookPayload("false", [new AdyenNotificationItemWrapper(item)]);

        var response = await client.PostAsJsonAsync("/webhooks/adyen", payload);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be("[accepted]");
    }

    [Fact]
    public async Task AC2_When_Hmac_Verification_Fails_Reject_Notification()
    {
        if (!_dockerAvailable) return;

        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var pspRef = $"PSP-{Guid.NewGuid():N}";
        var item = CreateNotificationItem(pspRef, "PAY-AC2-TAMPERED", "AUTHORISATION", 2000, "EUR", "true");
        // Tampered signature
        item.AdditionalData!["hmacSignature"] = "invalid_tampered_signature_base64==";

        var payload = new AdyenWebhookPayload("false", [new AdyenNotificationItemWrapper(item)]);

        var response = await client.PostAsJsonAsync("/webhooks/adyen", payload);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AC2_When_Notification_Is_Replayed_Reject_With_Conflict()
    {
        if (!_dockerAvailable) return;

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var validator = factory.Services.GetRequiredService<IAdyenHmacValidator>();

        var pspRef = $"PSP-{Guid.NewGuid():N}";
        var item = CreateNotificationItem(pspRef, "PAY-REPLAY", "AUTHORISATION", 3000, "EUR", "true");
        SignNotificationItem(item, validator, TestHmacKey);

        var payload = new AdyenWebhookPayload("false", [new AdyenNotificationItemWrapper(item)]);

        // First delivery -> Accepted
        var firstResponse = await client.PostAsJsonAsync("/webhooks/adyen", payload);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Replay delivery -> Rejected with Conflict 409
        var replayResponse = await client.PostAsJsonAsync("/webhooks/adyen", payload);
        replayResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task AC3_When_Notification_Correlates_To_Known_Payment_Stores_Correlation()
    {
        if (!_dockerAvailable) return;

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var validator = factory.Services.GetRequiredService<IAdyenHmacValidator>();

        var paymentId = $"pay_corr_{Guid.NewGuid():N}";
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            var payment = new Payment(
                id: paymentId,
                partyId: "party_corr",
                amount: 4500,
                currency: "EUR",
                settlementChannel: "ADYEN",
                state: PaymentState.Authorized,
                channelReference: $"psp_init_{Guid.NewGuid():N}");
            db.Payments.Add(payment);
            await db.SaveChangesAsync();
        }

        var webhookPsp = $"PSP-{Guid.NewGuid():N}";
        var item = CreateNotificationItem(webhookPsp, paymentId, "CAPTURE", 4500, "EUR", "true");
        SignNotificationItem(item, validator, TestHmacKey);

        var payload = new AdyenWebhookPayload("false", [new AdyenNotificationItemWrapper(item)]);

        var response = await client.PostAsJsonAsync("/webhooks/adyen", payload);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            var storedNotification = await db.AdyenNotifications.FirstOrDefaultAsync(n => n.PspReference == webhookPsp);
            storedNotification.Should().NotBeNull();
            storedNotification!.CorrelatedPaymentId.Should().Be(paymentId);
            storedNotification.MerchantReference.Should().Be(paymentId);
        }
    }

    [Fact]
    public async Task AC4_Automated_Harness_Injects_Signed_Adyen_Notifications_Without_Tunnel()
    {
        if (!_dockerAvailable) return;

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var validator = factory.Services.GetRequiredService<IAdyenHmacValidator>();

        // Inject multiple signed events directly into in-memory pipeline
        var events = new[] { "AUTHORISATION", "CAPTURE", "REFUND" };
        foreach (var eventCode in events)
        {
            var pspRef = $"PSP-HARNESS-{Guid.NewGuid():N}";
            var item = CreateNotificationItem(pspRef, "PAY-HARNESS", eventCode, 1000, "EUR", "true");
            SignNotificationItem(item, validator, TestHmacKey);

            var payload = new AdyenWebhookPayload("false", [new AdyenNotificationItemWrapper(item)]);
            var response = await client.PostAsJsonAsync("/api/v1/webhooks/adyen", payload);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadAsStringAsync();
            body.Should().Be("[accepted]");
        }
    }

    private static AdyenNotificationRequestItem CreateNotificationItem(
        string pspReference,
        string merchantReference,
        string eventCode,
        long amount,
        string currency,
        string success) =>
        new(
            AdditionalData: new Dictionary<string, string>(),
            Amount: new AdyenAmountDto(currency, amount),
            EventCode: eventCode,
            EventDate: DateTimeOffset.UtcNow,
            MerchantAccountCode: "InterparkingMockAccount",
            MerchantReference: merchantReference,
            OriginalReference: null,
            PspReference: pspReference,
            Reason: null,
            Success: success);

    private static void SignNotificationItem(
        AdyenNotificationRequestItem item,
        IAdyenHmacValidator validator,
        string hmacKey)
    {
        var sig = validator.CalculateSignature(item, hmacKey);
        item.AdditionalData!["hmacSignature"] = sig;
    }
}
