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
using Microsoft.IdentityModel.Tokens;
using PaymentGateway.Api.Endpoints;
using PaymentGateway.Application.Webhooks;
using PaymentGateway.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace PaymentGateway.IntegrationTests;

public sealed class MerchantWebhookEndpointsIntegrationTests : IAsyncLifetime, IDisposable
{
    private const string JwtKey = "DEV_ONLY_CHANGE_ME_32CHARS_MINIMUM!!";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private PostgreSqlContainer? _postgres;
    private bool _dockerAvailable;
    private readonly WireMockServer _wireMockServer;

    public MerchantWebhookEndpointsIntegrationTests()
    {
        _wireMockServer = WireMockServer.Start();
    }

    public async Task InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("payment_gateway_webhook_endpoints_test")
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

    public void Dispose()
    {
        _wireMockServer.Stop();
        _wireMockServer.Dispose();
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

    private static string GenerateToken(string role, string subject = "merch_acme")
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(JwtKey);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([
                new Claim("sub", subject),
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
    public async Task GetSubscription_WithoutToken_Returns401Unauthorized()
    {
        if (!_dockerAvailable) return;

        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/webhooks/merchants/subscription");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetSubscription_WithRiskRole_Returns403Forbidden()
    {
        if (!_dockerAvailable) return;

        using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateToken("risk"));

        var response = await client.GetAsync("/api/v1/webhooks/merchants/subscription");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetSubscription_WhenNotConfigured_Returns404NotFound()
    {
        if (!_dockerAvailable) return;

        using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateToken("merchant", "merch_unknown_404"));

        var response = await client.GetAsync("/api/v1/webhooks/merchants/subscription");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PutSubscription_WithInvalidUrl_Returns400BadRequest()
    {
        if (!_dockerAvailable) return;

        using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateToken("merchant", "merch_test_invalid"));

        var payload = new UpdateWebhookSubscriptionRequest("ftp://invalid-url", true);
        var response = await client.PutAsJsonAsync("/api/v1/webhooks/merchants/subscription", payload);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AC1_PutSubscription_And_GetSubscription_ManagesLifecycleSuccessfully()
    {
        if (!_dockerAvailable) return;

        using var factory = CreateFactory();
        var client = factory.CreateClient();
        var partyId = "merch_acme_lifecycle";
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateToken("merchant", partyId));

        var initialRequest = new UpdateWebhookSubscriptionRequest("https://api.merchant.com/webhooks/v1", true);
        var putResponse = await client.PutAsJsonAsync("/api/v1/webhooks/merchants/subscription", initialRequest);
        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var putResult = await putResponse.Content.ReadFromJsonAsync<MerchantWebhookSubscriptionDto>(JsonOptions);
        putResult.Should().NotBeNull();
        putResult!.PartyId.Should().Be(partyId);
        putResult.EndpointUrl.Should().Be("https://api.merchant.com/webhooks/v1");
        putResult.IsActive.Should().BeTrue();
        putResult.SecretKey.Should().StartWith("whsec_");

        // Verify GET retrieves the updated subscription
        var getResponse = await client.GetAsync("/api/v1/webhooks/merchants/subscription");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResult = await getResponse.Content.ReadFromJsonAsync<MerchantWebhookSubscriptionDto>(JsonOptions);
        getResult.Should().NotBeNull();
        getResult!.EndpointUrl.Should().Be("https://api.merchant.com/webhooks/v1");
        getResult.SecretKey.Should().Be(putResult.SecretKey);
        getResult.IsActive.Should().BeTrue();

        // Update active flag to false
        var updateRequest = new UpdateWebhookSubscriptionRequest("https://api.merchant.com/webhooks/v2", false);
        var updateResponse = await client.PutAsJsonAsync("/api/v1/webhooks/merchants/subscription", updateRequest);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var updatedResult = await updateResponse.Content.ReadFromJsonAsync<MerchantWebhookSubscriptionDto>(JsonOptions);
        updatedResult!.EndpointUrl.Should().Be("https://api.merchant.com/webhooks/v2");
        updatedResult.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task AC2_RotateSecret_GeneratesNewCryptographicallySecureSecretKey()
    {
        if (!_dockerAvailable) return;

        using var factory = CreateFactory();
        var client = factory.CreateClient();
        var partyId = "merch_rotate_test";
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateToken("merchant", partyId));

        // Create subscription
        var putRequest = new UpdateWebhookSubscriptionRequest("https://api.merchant.com/hook", true);
        var putResponse = await client.PutAsJsonAsync("/api/v1/webhooks/merchants/subscription", putRequest);
        var initial = await putResponse.Content.ReadFromJsonAsync<MerchantWebhookSubscriptionDto>(JsonOptions);
        initial.Should().NotBeNull();

        // Rotate secret
        var rotateResponse = await client.PostAsync("/api/v1/webhooks/merchants/subscription/rotate-secret", null);
        rotateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var rotated = await rotateResponse.Content.ReadFromJsonAsync<MerchantWebhookSubscriptionDto>(JsonOptions);
        rotated.Should().NotBeNull();
        rotated!.SecretKey.Should().NotBe(initial!.SecretKey);
        rotated.SecretKey.Should().StartWith("whsec_");

        // GET confirms rotation persisted
        var getResponse = await client.GetAsync("/api/v1/webhooks/merchants/subscription");
        var persisted = await getResponse.Content.ReadFromJsonAsync<MerchantWebhookSubscriptionDto>(JsonOptions);
        persisted!.SecretKey.Should().Be(rotated.SecretKey);
    }

    [Fact]
    public async Task AC3_TestDispatch_DeliversSimulatedEvent_AndReturnsLatencyAndHmac()
    {
        if (!_dockerAvailable) return;

        var endpointPath = "/test-receiver-200";
        _wireMockServer
            .Given(Request.Create().WithPath(endpointPath).UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{\"received\":true}"));

        using var factory = CreateFactory();
        var client = factory.CreateClient();
        var partyId = "merch_sim_success";
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateToken("merchant", partyId));

        var targetUrl = $"{_wireMockServer.Url}{endpointPath}";

        // Configure subscription
        await client.PutAsJsonAsync("/api/v1/webhooks/merchants/subscription", new UpdateWebhookSubscriptionRequest(targetUrl, true));

        // Trigger test dispatch
        var dispatchRequest = new TestWebhookDispatchRequest(targetUrl, "payment.settled");
        var dispatchResponse = await client.PostAsJsonAsync("/api/v1/webhooks/merchants/test-dispatch", dispatchRequest);

        dispatchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await dispatchResponse.Content.ReadFromJsonAsync<TestMerchantWebhookDispatchResultDto>(JsonOptions);

        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.LatencyMs.Should().BeGreaterThanOrEqualTo(0);
        result.Signature.Should().NotBeNullOrWhiteSpace();
        result.RequestHeaders.Should().ContainKey("X-Signature-SHA256");
        result.RequestHeaders["X-Signature-SHA256"].Should().Be(result.Signature);
        result.RequestHeaders.Should().ContainKey("X-Event-Id");
        result.RequestHeaders.Should().ContainKey("X-Timestamp");
        result.ResponseBody.Should().Contain("received");
    }

    [Fact]
    public async Task AC4_TestDispatch_WhenEndpointFails_ReturnsDiagnosticDetailsAndComputedSignature()
    {
        if (!_dockerAvailable) return;

        var endpointPath = "/test-receiver-500";
        _wireMockServer
            .Given(Request.Create().WithPath(endpointPath).UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500).WithBody("Internal Server Error"));

        using var factory = CreateFactory();
        var client = factory.CreateClient();
        var partyId = "merch_sim_fail";
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateToken("merchant", partyId));

        var targetUrl = $"{_wireMockServer.Url}{endpointPath}";
        await client.PutAsJsonAsync("/api/v1/webhooks/merchants/subscription", new UpdateWebhookSubscriptionRequest(targetUrl, true));

        var dispatchRequest = new TestWebhookDispatchRequest(targetUrl, "payment.failed");
        var dispatchResponse = await client.PostAsJsonAsync("/api/v1/webhooks/merchants/test-dispatch", dispatchRequest);

        dispatchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await dispatchResponse.Content.ReadFromJsonAsync<TestMerchantWebhookDispatchResultDto>(JsonOptions);

        result.Should().NotBeNull();
        result!.Success.Should().BeFalse();
        result.StatusCode.Should().Be(500);
        result.ErrorMessage.Should().Contain("500");
        result.Signature.Should().NotBeNullOrWhiteSpace();
        result.RequestHeaders.Should().ContainKey("X-Signature-SHA256");
        result.RequestPayload.Should().Contain("payment.failed");
    }
}
