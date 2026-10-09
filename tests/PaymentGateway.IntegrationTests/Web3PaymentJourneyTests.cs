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
using Microsoft.IdentityModel.Tokens;
using PaymentGateway.Api.Endpoints;
using PaymentGateway.Application.Payments;
using PaymentGateway.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace PaymentGateway.IntegrationTests;

public sealed class Web3PaymentJourneyTests : IAsyncLifetime
{
    private const string JwtKey = "DEV_ONLY_CHANGE_ME_32CHARS_MINIMUM!!";
    private PostgreSqlContainer? _postgres;
    private bool _dockerAvailable;

    public async Task InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("payment_gateway_web3_test")
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
                        ["PaymentGateway:ActiveChannel"] = "WEB3",
                        ["Web3:SecretlessMode"] = "true",
                        ["Web3:SupportedAssets:0"] = "USDC"
                    });
                });
            });
    }

    private static string GenerateTestToken()
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(JwtKey);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([
                new Claim("sub", "merchant_web3_test"),
                new Claim("role", "merchant")
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
    public async Task Web3_Authorize_And_Capture_Completes_Successfully()
    {
        if (!_dockerAvailable || _postgres is null) return;

        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateTestToken());

        // 1. Authorize with USDC and native 6-decimal precision (e.g. 10.50 USDC = 10,500,000 minor units)
        var authRequest = new AuthorizePaymentRequest(
            PartyId: "party_web3_1",
            Amount: 10_500_000,
            Currency: "USDC",
            IdempotencyKey: $"idem_web3_auth_{Guid.NewGuid():N}");

        var authResponse = await client.PostAsJsonAsync("/payments", authRequest);
        authResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var payment = await authResponse.Content.ReadFromJsonAsync<PaymentDto>();
        payment.Should().NotBeNull();
        payment!.Currency.Should().Be("USDC");
        payment.Amount.Should().Be(10_500_000);
        payment.SettlementChannel.Should().Be("WEB3");
        payment.State.Should().Be("Authorized");

        // 2. Capture
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"idem_web3_cap_{Guid.NewGuid():N}");
        var captureResponse = await client.PostAsync($"/payments/{payment.PaymentId}/capture", null);

        captureResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var capturedPayment = await captureResponse.Content.ReadFromJsonAsync<PaymentDto>();
        capturedPayment.Should().NotBeNull();
        capturedPayment!.State.Should().Be("Captured");
        capturedPayment.ChannelReference.Should().StartWith("0x");
    }

    [Fact]
    public async Task Web3_Authorize_And_Cancel_Completes_OffChain()
    {
        if (!_dockerAvailable || _postgres is null) return;

        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateTestToken());

        var authRequest = new AuthorizePaymentRequest(
            PartyId: "party_web3_cancel",
            Amount: 5_000_000,
            Currency: "USDC",
            IdempotencyKey: $"idem_web3_cancel_auth_{Guid.NewGuid():N}");

        var authResponse = await client.PostAsJsonAsync("/payments", authRequest);
        authResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var payment = await authResponse.Content.ReadFromJsonAsync<PaymentDto>();

        // Cancel
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"idem_web3_cnc_{Guid.NewGuid():N}");
        var cancelResponse = await client.PostAsync($"/payments/{payment!.PaymentId}/cancel", null);

        cancelResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var cancelledPayment = await cancelResponse.Content.ReadFromJsonAsync<PaymentDto>();
        cancelledPayment.Should().NotBeNull();
        cancelledPayment!.State.Should().Be("Cancelled");
    }

    [Fact]
    public async Task Web3_Authorize_Capture_And_Refund_Completes_Successfully()
    {
        if (!_dockerAvailable || _postgres is null) return;

        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateTestToken());

        // Authorize
        var authResponse = await client.PostAsJsonAsync("/payments", new AuthorizePaymentRequest(
            PartyId: "party_web3_refund",
            Amount: 7_500_000,
            Currency: "USDC",
            IdempotencyKey: $"idem_web3_ref_auth_{Guid.NewGuid():N}"));
        authResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var payment = await authResponse.Content.ReadFromJsonAsync<PaymentDto>();

        // Capture
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"idem_web3_ref_cap_{Guid.NewGuid():N}");
        var captureResponse = await client.PostAsync($"/payments/{payment!.PaymentId}/capture", null);
        captureResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Refund
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"idem_web3_ref_ref_{Guid.NewGuid():N}");
        var refundResponse = await client.PostAsync($"/payments/{payment.PaymentId}/refund", null);
        refundResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var refundedPayment = await refundResponse.Content.ReadFromJsonAsync<PaymentDto>();
        refundedPayment.Should().NotBeNull();
        refundedPayment!.State.Should().Be("Refunded");
        refundedPayment.ChannelReference.Should().StartWith("0x");
    }
}
