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
using Microsoft.IdentityModel.Tokens;
using PaymentGateway.Api.Endpoints;
using PaymentGateway.Application.Payments;
using PaymentGateway.Application.Web3;
using PaymentGateway.Infrastructure.Persistence;
using PaymentGateway.Infrastructure.Web3;
using Testcontainers.PostgreSql;
using Xunit;

namespace PaymentGateway.IntegrationTests;

public sealed class Web3ConfirmationAndFinalityIntegrationTests : IAsyncLifetime
{
    private const string JwtKey = "DEV_ONLY_CHANGE_ME_32CHARS_MINIMUM!!";
    private PostgreSqlContainer? _postgres;
    private bool _dockerAvailable;

    public async Task InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("payment_gateway_web3_finality_test")
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

    private WebApplicationFactory<Program> CreateFactory(int requiredConfirmations = 3, int? initialConfirmations = 1)
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
                        ["PaymentGateway:ActiveChannel"] = "WEB3",
                        ["Web3:SecretlessMode"] = "true",
                        ["Web3:SupportedAssets:0"] = "USDC",
                        ["Web3:RequiredFinalityConfirmations"] = requiredConfirmations.ToString(),
                        ["Web3:InitialConfirmations"] = initialConfirmations?.ToString()
                    });
                });
            });
    }

    private static HttpClient CreateAuthenticatedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var token = GenerateJwtToken();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string GenerateJwtToken()
    {
        var handler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(JwtKey);
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim("sub", "integration-test-user"),
                new Claim("role", "merchant")
            }),
            Expires = DateTime.UtcNow.AddHours(1),
            Issuer = "payment-gateway",
            Audience = "payment-gateway",
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        var token = handler.CreateToken(descriptor);
        return handler.WriteToken(token);
    }

    [Fact]
    public async Task AC1_AC2_AC4_CaptureAwaitingFinalityRemainsPending_ThenSettlesWhenFinalityMet()
    {
        if (!_dockerAvailable) return;

        using var factory = CreateFactory(requiredConfirmations: 3, initialConfirmations: 1);
        using var client = CreateAuthenticatedClient(factory);

        // 1. Authorize USDC payment off-chain
        var authRequest = new AuthorizePaymentRequest(
            IdempotencyKey: $"web3_finality_auth_{Guid.NewGuid():N}",
            PartyId: "merchant_party_1",
            Amount: 10000000L, // 10 USDC
            Currency: "USDC");

        var authResponse = await client.PostAsJsonAsync("/payments", authRequest);
        authResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var payment = await authResponse.Content.ReadFromJsonAsync<PaymentDto>();
        payment.Should().NotBeNull();
        payment!.State.Should().Be("Authorized");

        // 2. Capture payment on-chain (broadcasts tx, but has 1 of 3 required confirmations) - AC-1, AC-4
        using var captureReq = new HttpRequestMessage(HttpMethod.Post, $"/payments/{payment.PaymentId}/capture");
        captureReq.Headers.Add("Idempotency-Key", $"web3_finality_cap_{Guid.NewGuid():N}");
        var captureResponse = await client.SendAsync(captureReq);
        captureResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var capturePayment = await captureResponse.Content.ReadFromJsonAsync<PaymentDto>();
        capturePayment.Should().NotBeNull();
        // AC-1, AC-4: State remains Pending and does not report as captured
        capturePayment!.State.Should().Be("Pending");
        capturePayment.ChannelReference.Should().StartWith("0x");

        var txHash = capturePayment.ChannelReference!;

        // Check GET /payments/{id}
        var getResponse = await client.GetAsync($"/payments/{payment.PaymentId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var getPayment = await getResponse.Content.ReadFromJsonAsync<PaymentDto>();
        getPayment!.State.Should().Be("Pending");

        // Check GET /ops/web3/pending
        var pendingResponse = await client.GetAsync("/ops/web3/pending");
        pendingResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var pendingList = await pendingResponse.Content.ReadFromJsonAsync<List<PendingObservationDto>>();
        pendingList.Should().Contain(p => p.PaymentId == payment.PaymentId && p.Status == Web3SettlementStatusExtensions.Pending && p.ConfirmationDepth == 1);

        // 3. Advance confirmations to 3 (finality met) - AC-2
        var simulator = factory.Services.GetRequiredService<IWeb3ChainClient>() as LocalWeb3SimulatorClient;
        simulator.Should().NotBeNull();
        simulator!.SetConfirmations(txHash, 3);

        // Trigger confirm check via /ops/web3/payments/{id}/confirm
        var confirmResponse = await client.PostAsync($"/ops/web3/payments/{payment.PaymentId}/confirm", null);
        confirmResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var confirmResult = await confirmResponse.Content.ReadFromJsonAsync<PendingObservationDto>();
        confirmResult!.Status.Should().Be(Web3SettlementStatusExtensions.Settled);
        confirmResult.ConfirmationDepth.Should().Be(3);

        // Verify payment is now Captured, retaining txHash (AC-2)
        var finalGetResponse = await client.GetAsync($"/payments/{payment.PaymentId}");
        finalGetResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var finalPayment = await finalGetResponse.Content.ReadFromJsonAsync<PaymentDto>();
        finalPayment!.State.Should().Be("Captured");
        finalPayment.ChannelReference.Should().Be(txHash);
    }

    [Fact]
    public async Task AC3_WhenTransactionReorganizedBeforeFinality_PaymentRemainsPending_AndDoesNotComplete()
    {
        if (!_dockerAvailable) return;

        using var factory = CreateFactory(requiredConfirmations: 3, initialConfirmations: 1);
        using var client = CreateAuthenticatedClient(factory);

        // 1. Authorize
        var authRequest = new AuthorizePaymentRequest(
            IdempotencyKey: $"web3_reorg_auth_{Guid.NewGuid():N}",
            PartyId: "merchant_party_reorg",
            Amount: 5000000L,
            Currency: "USDC");

        var authResponse = await client.PostAsJsonAsync("/payments", authRequest);
        var payment = await authResponse.Content.ReadFromJsonAsync<PaymentDto>();

        // 2. Capture (pending finality)
        using var captureReq = new HttpRequestMessage(HttpMethod.Post, $"/payments/{payment!.PaymentId}/capture");
        captureReq.Headers.Add("Idempotency-Key", $"web3_reorg_cap_{Guid.NewGuid():N}");
        var captureResponse = await client.SendAsync(captureReq);
        var capturePayment = await captureResponse.Content.ReadFromJsonAsync<PaymentDto>();
        capturePayment!.State.Should().Be("Pending");

        var txHash = capturePayment.ChannelReference!;

        // 3. Simulate pre-finality reorganization (drop receipt from simulator) - AC-3
        var simulator = factory.Services.GetRequiredService<IWeb3ChainClient>() as LocalWeb3SimulatorClient;
        simulator!.SimulateReorg(txHash);

        // Trigger confirm check
        var confirmResponse = await client.PostAsync($"/ops/web3/payments/{payment.PaymentId}/confirm", null);
        confirmResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var confirmResult = await confirmResponse.Content.ReadFromJsonAsync<PendingObservationDto>();
        confirmResult!.Status.Should().Be(Web3SettlementStatusExtensions.ReorgPending);

        // Verify payment remains Pending (does not transition to Captured)
        var getResponse = await client.GetAsync($"/payments/{payment.PaymentId}");
        var currentPayment = await getResponse.Content.ReadFromJsonAsync<PaymentDto>();
        currentPayment!.State.Should().Be("Pending");
    }

    [Fact]
    public async Task WhenOnChainTransactionReverts_RestoresPriorState()
    {
        if (!_dockerAvailable) return;

        using var factory = CreateFactory(requiredConfirmations: 3, initialConfirmations: 1);
        using var client = CreateAuthenticatedClient(factory);

        // 1. Authorize USDC payment off-chain
        var authRequest = new AuthorizePaymentRequest(
            IdempotencyKey: $"web3_revert_auth_{Guid.NewGuid():N}",
            PartyId: "merchant_party_revert",
            Amount: 10000000L, // 10 USDC
            Currency: "USDC");

        var authResponse = await client.PostAsJsonAsync("/payments", authRequest);
        authResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var payment = await authResponse.Content.ReadFromJsonAsync<PaymentDto>();
        payment.Should().NotBeNull();
        payment!.State.Should().Be("Authorized");

        // 2. Capture payment on-chain (broadcasts tx, enters Pending)
        using var captureReq = new HttpRequestMessage(HttpMethod.Post, $"/payments/{payment.PaymentId}/capture");
        captureReq.Headers.Add("Idempotency-Key", $"web3_revert_cap_{Guid.NewGuid():N}");
        var captureResponse = await client.SendAsync(captureReq);
        captureResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var capturePayment = await captureResponse.Content.ReadFromJsonAsync<PaymentDto>();
        capturePayment!.State.Should().Be("Pending");

        var txHash = capturePayment.ChannelReference!;

        // 3. Simulate on-chain revert with finality
        var simulator = factory.Services.GetRequiredService<IWeb3ChainClient>() as LocalWeb3SimulatorClient;
        simulator.Should().NotBeNull();
        simulator!.SimulateRevert(txHash);
        simulator.SetConfirmations(txHash, 3);

        // 4. Trigger watcher via /ops/web3/payments/{id}/confirm
        var confirmResponse = await client.PostAsync($"/ops/web3/payments/{payment.PaymentId}/confirm", null);
        confirmResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var confirmResult = await confirmResponse.Content.ReadFromJsonAsync<PendingObservationDto>();
        confirmResult!.Status.Should().Be(Web3SettlementStatusExtensions.Settled);

        // 5. Verify payment state was restored to Authorized
        var finalGetResponse = await client.GetAsync($"/payments/{payment.PaymentId}");
        finalGetResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var finalPayment = await finalGetResponse.Content.ReadFromJsonAsync<PaymentDto>();
        finalPayment!.State.Should().Be("Authorized");
    }

    [Fact]
    public async Task OpsWeb3_WatchAll_ExecutesWatcherSweep()
    {
        if (!_dockerAvailable) return;

        using var factory = CreateFactory();
        using var client = CreateAuthenticatedClient(factory);

        var sweepResponse = await client.PostAsync("/ops/web3/watch-all", null);
        sweepResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private sealed record PendingObservationDto(
        string PaymentId,
        string TransactionHash,
        int ConfirmationDepth,
        int RequiredConfirmations,
        string Status,
        string? Message);
}
