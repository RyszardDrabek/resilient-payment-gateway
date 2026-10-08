using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PaymentGateway.Application.Adyen;
using PaymentGateway.Application.Events;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Infrastructure.Adyen;
using PaymentGateway.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace PaymentGateway.IntegrationTests.Adyen;

public sealed class AdyenReconciliationIntegrationTests : IAsyncLifetime
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
                .WithDatabase("payment_gateway_test_reconcile")
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
                        ["PaymentGateway:ActiveChannel"] = "ADYEN",
                        ["Adyen:HmacKey"] = TestHmacKey,
                        ["Adyen:AutoStartMockServer"] = "true"
                    });
                });
            });
    }

    [Fact]
    public async Task AC1_When_Notifications_Arrive_OutOfOrder_Or_Duplicated_Leaves_Latest_Acquirer_Outcome()
    {
        if (!_dockerAvailable) return;

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var validator = scope.ServiceProvider.GetRequiredService<IAdyenHmacValidator>();

        // 1. Seed Authorized payment
        var payment = Payment.Authorize("cust_ac1", 5000L, "EUR", "ADYEN", "psp_auth_orig");
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        // 2. Webhook CAPTURE arrives -> payment transitions to Captured
        var capPsp = $"PSP-CAP-{Guid.NewGuid():N}";
        var capItem = CreateNotificationItem(capPsp, payment.Id, "CAPTURE", 5000, "EUR", "true", originalReference: "psp_auth_orig");
        SignNotificationItem(capItem, validator, TestHmacKey);
        var capPayload = new AdyenWebhookPayload("false", [new AdyenNotificationItemWrapper(capItem)]);

        var capResp = await client.PostAsJsonAsync("/webhooks/adyen", capPayload);
        capResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var paymentAfterCap = await db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == payment.Id);
        paymentAfterCap!.State.Should().Be(PaymentState.Captured);
        var capturedVersion = paymentAfterCap.Version;

        // 3. Webhook duplicate CAPTURE arrives (different pspRef to pass replay check) -> does not double-apply
        var dupPsp = $"PSP-CAP-DUP-{Guid.NewGuid():N}";
        var dupItem = CreateNotificationItem(dupPsp, payment.Id, "CAPTURE", 5000, "EUR", "true", originalReference: "psp_auth_orig");
        SignNotificationItem(dupItem, validator, TestHmacKey);
        var dupPayload = new AdyenWebhookPayload("false", [new AdyenNotificationItemWrapper(dupItem)]);

        var dupResp = await client.PostAsJsonAsync("/webhooks/adyen", dupPayload);
        dupResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var paymentAfterDup = await db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == payment.Id);
        paymentAfterDup!.State.Should().Be(PaymentState.Captured);
        paymentAfterDup.Version.Should().Be(capturedVersion); // Version did NOT increment

        // 4. Late out-of-order AUTHORISATION arrives -> does NOT revert Captured to Authorized
        var lateAuthPsp = $"PSP-AUTH-LATE-{Guid.NewGuid():N}";
        var lateAuthItem = CreateNotificationItem(lateAuthPsp, payment.Id, "AUTHORISATION", 5000, "EUR", "true");
        SignNotificationItem(lateAuthItem, validator, TestHmacKey);
        var lateAuthPayload = new AdyenWebhookPayload("false", [new AdyenNotificationItemWrapper(lateAuthItem)]);

        var lateAuthResp = await client.PostAsJsonAsync("/webhooks/adyen", lateAuthPayload);
        lateAuthResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var paymentFinal = await db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == payment.Id);
        paymentFinal!.State.Should().Be(PaymentState.Captured); // Acquirer truth preserved!
    }

    [Fact]
    public async Task AC2_When_Payment_Is_Pending_Later_Verified_Notification_Resolves_Payment()
    {
        if (!_dockerAvailable) return;

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var validator = scope.ServiceProvider.GetRequiredService<IAdyenHmacValidator>();

        // Seed pending payment
        var payment = Payment.CreatePending("cust_pending_ac2", 7500L, "EUR", "ADYEN", "tmp_chan_ref");
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        // Webhook arrives matching payment.Id as merchantReference
        var pspRef = $"PSP-RESOLVE-{Guid.NewGuid():N}";
        var item = CreateNotificationItem(pspRef, payment.Id, "AUTHORISATION", 7500, "EUR", "true");
        SignNotificationItem(item, validator, TestHmacKey);
        var payload = new AdyenWebhookPayload("false", [new AdyenNotificationItemWrapper(item)]);

        var response = await client.PostAsJsonAsync("/webhooks/adyen", payload);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updatedPayment = await db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == payment.Id);
        updatedPayment!.State.Should().Be(PaymentState.Authorized);
        updatedPayment.ChannelReference.Should().Be(pspRef);
    }

    [Fact]
    public async Task AC3_When_Verified_Notification_Cannot_Be_Correlated_Retains_For_Ops_And_Does_Not_Invent_Payment()
    {
        if (!_dockerAvailable) return;

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var validator = scope.ServiceProvider.GetRequiredService<IAdyenHmacValidator>();

        var initialPaymentCount = await db.Payments.CountAsync();

        var orphanPsp = $"PSP-ORPHAN-{Guid.NewGuid():N}";
        var orphanItem = CreateNotificationItem(orphanPsp, "non_existent_merchant_ref", "AUTHORISATION", 3000, "EUR", "true");
        SignNotificationItem(orphanItem, validator, TestHmacKey);
        var payload = new AdyenWebhookPayload("false", [new AdyenNotificationItemWrapper(orphanItem)]);

        var response = await client.PostAsJsonAsync("/webhooks/adyen", payload);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Does NOT invent a payment
        var currentPaymentCount = await db.Payments.CountAsync();
        currentPaymentCount.Should().Be(initialPaymentCount);

        // Retained for ops visibility
        var opsResponse = await client.GetAsync("/ops/reconciliation/uncorrelated");
        opsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var uncorrelatedList = await opsResponse.Content.ReadFromJsonAsync<JsonElement>();
        uncorrelatedList.EnumerateArray().Any(n => n.GetProperty("pspReference").GetString() == orphanPsp).Should().BeTrue();
    }

    [Fact]
    public async Task AC4_When_No_Notification_Arrives_Status_Query_Resolves_Payment_Or_Exposes_Unresolved()
    {
        if (!_dockerAvailable) return;

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();

        // 1. Payment that can resolve via WireMock query
        var resolvablePayment = Payment.CreatePending("cust_ac4_1", 2000L, "EUR", "ADYEN", "adyen_auth_resolvable");
        db.Payments.Add(resolvablePayment);

        // 2. Payment that stays unresolved in WireMock query
        var unresolvedPayment = Payment.CreatePending("cust_ac4_2", 4000L, "EUR", "ADYEN", "unresolved_psp_ref");
        db.Payments.Add(unresolvedPayment);
        await db.SaveChangesAsync();

        // Query status for resolvable payment
        var recResp1 = await client.PostAsync($"/ops/reconciliation/payments/{resolvablePayment.Id}/reconcile", null);
        recResp1.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated1 = await db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == resolvablePayment.Id);
        updated1!.State.Should().Be(PaymentState.Authorized);

        // Query status for unresolved payment
        var recResp2 = await client.PostAsync($"/ops/reconciliation/payments/{unresolvedPayment.Id}/reconcile", null);
        recResp2.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated2 = await db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == unresolvedPayment.Id);
        updated2!.State.Should().Be(PaymentState.Pending); // Still Pending!

        // Expose in ops unresolved endpoint
        var opsUnresolvedResp = await client.GetAsync("/ops/reconciliation/unresolved");
        opsUnresolvedResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var unresolvedList = await opsUnresolvedResp.Content.ReadFromJsonAsync<JsonElement>();
        unresolvedList.EnumerateArray().Any(p => p.GetProperty("id").GetString() == unresolvedPayment.Id).Should().BeTrue();
    }

    [Fact]
    public async Task AC5_When_Reconciliation_Resolves_Payment_Emits_PaymentLifecycleEvent_To_Outbox()
    {
        if (!_dockerAvailable) return;

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var validator = scope.ServiceProvider.GetRequiredService<IAdyenHmacValidator>();

        // Seed pending payment
        var payment = Payment.CreatePending("cust_ac5", 9000L, "EUR", "ADYEN", "tmp_ref");
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        // Clear outbox to observe reconciliation event specifically
        db.OutboxMessages.RemoveRange(db.OutboxMessages);
        await db.SaveChangesAsync();

        // Webhook arrives and resolves payment
        var pspRef = $"PSP-EVT-{Guid.NewGuid():N}";
        var item = CreateNotificationItem(pspRef, payment.Id, "AUTHORISATION", 9000, "EUR", "true");
        SignNotificationItem(item, validator, TestHmacKey);
        var payload = new AdyenWebhookPayload("false", [new AdyenNotificationItemWrapper(item)]);

        var response = await client.PostAsJsonAsync("/webhooks/adyen", payload);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert: Outbox record created for EVT publication
        var outboxMessage = await db.OutboxMessages
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.EventType == PaymentLifecycleEvent.EventType);

        outboxMessage.Should().NotBeNull();
        var lifecycleEvent = JsonSerializer.Deserialize<PaymentLifecycleEvent>(outboxMessage!.Payload);
        lifecycleEvent.Should().NotBeNull();
        lifecycleEvent!.PaymentId.Should().Be(payment.Id);
        lifecycleEvent.Outcome.Should().Be("authorized");
        lifecycleEvent.Amount.Should().Be(9000L);
    }

    private static AdyenNotificationRequestItem CreateNotificationItem(
        string pspReference,
        string merchantReference,
        string eventCode,
        long amountValue,
        string currency,
        string success,
        string? originalReference = null)
    {
        return new AdyenNotificationRequestItem(
            PspReference: pspReference,
            OriginalReference: originalReference,
            MerchantAccountCode: "InterparkingMockAccount",
            MerchantReference: merchantReference,
            Amount: new AdyenAmountDto(currency, amountValue),
            EventCode: eventCode,
            EventDate: DateTimeOffset.UtcNow,
            Success: success,
            Reason: null,
            AdditionalData: new Dictionary<string, string>());
    }

    private static void SignNotificationItem(
        AdyenNotificationRequestItem item,
        IAdyenHmacValidator validator,
        string hmacKey)
    {
        var sig = validator.CalculateSignature(item, hmacKey);
        item.AdditionalData!["hmacSignature"] = sig;
    }
}
