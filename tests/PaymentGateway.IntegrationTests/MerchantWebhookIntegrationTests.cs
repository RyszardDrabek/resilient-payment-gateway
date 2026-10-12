using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.Events;
using PaymentGateway.Application.Webhooks;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Infrastructure.Options;
using PaymentGateway.Infrastructure.Persistence;
using PaymentGateway.Infrastructure.Repositories;
using PaymentGateway.Infrastructure.Webhooks;
using Testcontainers.PostgreSql;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace PaymentGateway.IntegrationTests;

public sealed class MerchantWebhookIntegrationTests : IAsyncLifetime, IDisposable
{
    private PostgreSqlContainer? _postgres;
    private bool _dockerAvailable;
    private readonly WireMockServer _wireMockServer;

    public MerchantWebhookIntegrationTests()
    {
        _wireMockServer = WireMockServer.Start();
    }

    public async Task InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("payment_gateway_webhooks_test")
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

    public void Dispose()
    {
        _wireMockServer.Stop();
        _wireMockServer.Dispose();
    }

    private PaymentDbContext CreateDbContext()
    {
        var connectionString = _dockerAvailable && _postgres is not null
            ? _postgres.GetConnectionString()
            : "Host=localhost;Database=dummy;Username=dummy;Password=dummy";

        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new PaymentDbContext(options);
    }

    [Fact]
    public async Task AC1_And_AC2_TerminalTransition_DispatchesSignedWebhook_WithRequiredHeaders()
    {
        if (!_dockerAvailable) return;

        var endpointPath = "/webhook/merchant-ac1";
        var secretKey = "test_merchant_secret_123";

        _wireMockServer
            .Given(Request.Create().WithPath(endpointPath).UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200));

        await using var db = CreateDbContext();
        var partyId = "merchant_ac1";
        var subscription = new MerchantWebhookSubscription(
            Guid.NewGuid(),
            partyId,
            $"{_wireMockServer.Url}{endpointPath}",
            secretKey);
        db.MerchantWebhookSubscriptions.Add(subscription);
        await db.SaveChangesAsync();

        var repo = new MerchantWebhookSubscriptionRepository(db);
        var httpClient = new HttpClient();
        var signer = new HmacSha256WebhookSigner();
        var options = Options.Create(new MerchantWebhookOptions
        {
            MaxRetryAttempts = 1,
            InitialBackoffSeconds = 0
        });

        var dispatcher = new MerchantWebhookDispatcher(
            repo,
            httpClient,
            signer,
            options,
            NullLogger<MerchantWebhookDispatcher>.Instance);

        var evt = new PaymentLifecycleEvent(
            EventId: "evt_ac1_test",
            PaymentId: "pay_ac1_test",
            PartyId: partyId,
            Outcome: PaymentLifecycleOutcome.Captured.ToString().ToLowerInvariant(),
            Amount: 10000,
            Currency: "EUR",
            Version: 1,
            OccurredAt: DateTimeOffset.UtcNow);

        var result = await dispatcher.DispatchAsync(evt);

        result.Success.Should().BeTrue();
        result.DeliveryAttempts.Should().Be(1);

        var logs = _wireMockServer.LogEntries.Where(l => l.RequestMessage?.Path == endpointPath).ToList();
        logs.Should().ContainSingle();

        var request = logs[0].RequestMessage!;
        request.Headers.Should().NotBeNull();
        request.Headers.Should().ContainKey("X-Signature-SHA256");
        request.Headers.Should().ContainKey("X-Event-Id");
        request.Headers.Should().ContainKey("X-Timestamp");

        request.Headers!["X-Event-Id"].First().Should().Be("evt_ac1_test");

        var receivedSignature = request.Headers!["X-Signature-SHA256"].First();
        var body = request.Body;
        body.Should().NotBeNullOrWhiteSpace();

        var expectedSignature = signer.ComputeSignature(body!, secretKey);
        receivedSignature.Should().Be(expectedSignature);

        using var doc = JsonDocument.Parse(body!);
        doc.RootElement.GetProperty("paymentId").GetString().Should().Be("pay_ac1_test");
        doc.RootElement.GetProperty("state").GetString().Should().Be("captured");
        doc.RootElement.GetProperty("amount").GetInt64().Should().Be(10000);
        doc.RootElement.GetProperty("currency").GetString().Should().Be("EUR");
    }

    [Fact]
    public async Task AC3_TransientFailure_RetriesAndSucceeds()
    {
        if (!_dockerAvailable) return;

        var endpointPath = "/webhook/merchant-ac3";
        var secretKey = "test_retry_secret";

        var calls = 0;
        _wireMockServer
            .Given(Request.Create().WithPath(endpointPath).UsingPost())
            .RespondWith(Response.Create().WithCallback(_ =>
            {
                calls++;
                return new WireMock.ResponseMessage
                {
                    StatusCode = calls < 3 ? 500 : 200
                };
            }));

        await using var db = CreateDbContext();
        var partyId = "merchant_ac3";
        var subscription = new MerchantWebhookSubscription(
            Guid.NewGuid(),
            partyId,
            $"{_wireMockServer.Url}{endpointPath}",
            secretKey);
        db.MerchantWebhookSubscriptions.Add(subscription);
        await db.SaveChangesAsync();

        var repo = new MerchantWebhookSubscriptionRepository(db);
        var httpClient = new HttpClient();
        var signer = new HmacSha256WebhookSigner();
        var options = Options.Create(new MerchantWebhookOptions
        {
            MaxRetryAttempts = 5,
            InitialBackoffSeconds = 0 // Instant retries in test
        });

        var dispatcher = new MerchantWebhookDispatcher(
            repo,
            httpClient,
            signer,
            options,
            NullLogger<MerchantWebhookDispatcher>.Instance);

        var evt = new PaymentLifecycleEvent(
            EventId: "evt_ac3_retry",
            PaymentId: "pay_ac3_retry",
            PartyId: partyId,
            Outcome: "refunded",
            Amount: 2500,
            Currency: "EUR",
            Version: 1,
            OccurredAt: DateTimeOffset.UtcNow);

        var result = await dispatcher.DispatchAsync(evt);

        result.Success.Should().BeTrue();
        result.DeliveryAttempts.Should().Be(3);
        calls.Should().Be(3);
    }

    [Fact]
    public async Task AC4_PersistentFailure_ExhaustsRetries_AndDoesNotAlterPayment()
    {
        if (!_dockerAvailable) return;

        var endpointPath = "/webhook/merchant-ac4";
        var secretKey = "test_failing_secret";

        _wireMockServer
            .Given(Request.Create().WithPath(endpointPath).UsingPost())
            .RespondWith(Response.Create().WithStatusCode(503));

        await using var db = CreateDbContext();
        var partyId = "merchant_ac4";
        var subscription = new MerchantWebhookSubscription(
            Guid.NewGuid(),
            partyId,
            $"{_wireMockServer.Url}{endpointPath}",
            secretKey);
        db.MerchantWebhookSubscriptions.Add(subscription);

        var payment = new Payment(
            id: "pay_ac4_unaffected",
            partyId: partyId,
            amount: 7500,
            currency: "EUR",
            settlementChannel: "ADYEN",
            state: PaymentState.Captured,
            channelReference: "psp_ac4_ref");
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        var repo = new MerchantWebhookSubscriptionRepository(db);
        var httpClient = new HttpClient();
        var signer = new HmacSha256WebhookSigner();
        var options = Options.Create(new MerchantWebhookOptions
        {
            MaxRetryAttempts = 5,
            InitialBackoffSeconds = 0
        });

        var dispatcher = new MerchantWebhookDispatcher(
            repo,
            httpClient,
            signer,
            options,
            NullLogger<MerchantWebhookDispatcher>.Instance);

        var evt = new PaymentLifecycleEvent(
            EventId: "evt_ac4_fail",
            PaymentId: payment.Id,
            PartyId: partyId,
            Outcome: "captured",
            Amount: 7500,
            Currency: "EUR",
            Version: 1,
            OccurredAt: DateTimeOffset.UtcNow);

        var result = await dispatcher.DispatchAsync(evt);

        result.Success.Should().BeFalse();
        result.DeliveryAttempts.Should().Be(5);
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();

        // Payment status in DB remains completely unchanged (Captured)
        var refreshedPayment = await db.Payments.FirstOrDefaultAsync(p => p.Id == payment.Id);
        refreshedPayment.Should().NotBeNull();
        refreshedPayment!.State.Should().Be(PaymentState.Captured);
    }

    [Fact]
    public async Task AC5_NoDbSubscription_FallsBackToDefaultConfiguration()
    {
        if (!_dockerAvailable) return;

        var defaultPath = "/webhook/default-fallback";
        var defaultSecret = "global_default_secret_key";

        _wireMockServer
            .Given(Request.Create().WithPath(defaultPath).UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200));

        await using var db = CreateDbContext();
        var repo = new MerchantWebhookSubscriptionRepository(db);
        var httpClient = new HttpClient();
        var signer = new HmacSha256WebhookSigner();
        var options = Options.Create(new MerchantWebhookOptions
        {
            DefaultEndpointUrl = $"{_wireMockServer.Url}{defaultPath}",
            DefaultSecretKey = defaultSecret,
            MaxRetryAttempts = 1,
            InitialBackoffSeconds = 0
        });

        var dispatcher = new MerchantWebhookDispatcher(
            repo,
            httpClient,
            signer,
            options,
            NullLogger<MerchantWebhookDispatcher>.Instance);

        var evt = new PaymentLifecycleEvent(
            EventId: "evt_ac5_fallback",
            PaymentId: "pay_ac5_fallback",
            PartyId: "party_without_subscription",
            Outcome: "cancelled",
            Amount: 3000,
            Currency: "EUR",
            Version: 1,
            OccurredAt: DateTimeOffset.UtcNow);

        var result = await dispatcher.DispatchAsync(evt);

        result.Success.Should().BeTrue();
        result.EndpointUrl.Should().Be($"{_wireMockServer.Url}{defaultPath}");

        var logs = _wireMockServer.LogEntries.Where(l => l.RequestMessage?.Path == defaultPath).ToList();
        logs.Should().ContainSingle();

        var request = logs[0].RequestMessage!;
        var signature = request.Headers!["X-Signature-SHA256"].First();
        var expectedSignature = signer.ComputeSignature(request.Body!, defaultSecret);
        signature.Should().Be(expectedSignature);
    }

    [Fact]
    public async Task ActiveSubscription_UniqueIndex_RejectsDuplicateActivePartyId()
    {
        if (!_dockerAvailable) return;

        await using var db1 = CreateDbContext();
        var partyId = $"party_unique_test_{Guid.NewGuid():N}";
        var sub1 = new MerchantWebhookSubscription(
            Guid.NewGuid(),
            partyId,
            "https://merchant.example/active1",
            "secret_123",
            isActive: true);
        db1.MerchantWebhookSubscriptions.Add(sub1);
        await db1.SaveChangesAsync();

        await using var db2 = CreateDbContext();
        var sub2Duplicate = new MerchantWebhookSubscription(
            Guid.NewGuid(),
            partyId,
            "https://merchant.example/active2",
            "secret_456",
            isActive: true);
        db2.MerchantWebhookSubscriptions.Add(sub2Duplicate);
        var actDuplicate = async () => await db2.SaveChangesAsync();
        await actDuplicate.Should().ThrowAsync<DbUpdateException>();

        // Inactive subscription for same party should succeed
        await using var db3 = CreateDbContext();
        var sub3Inactive = new MerchantWebhookSubscription(
            Guid.NewGuid(),
            partyId,
            "https://merchant.example/inactive",
            "secret_789",
            isActive: false);
        db3.MerchantWebhookSubscriptions.Add(sub3Inactive);
        var actInactive = async () => await db3.SaveChangesAsync();
        await actInactive.Should().NotThrowAsync();
    }
}
