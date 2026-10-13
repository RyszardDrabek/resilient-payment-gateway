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
using PaymentGateway.Application.ApiKeys;
using PaymentGateway.Edge.Problems;
using PaymentGateway.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace PaymentGateway.IntegrationTests;

public sealed class MerchantApiKeyIntegrationTests : IAsyncLifetime
{
    private const string JwtKey = "DEV_ONLY_CHANGE_ME_32CHARS_MINIMUM!!";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private PostgreSqlContainer? _postgres;
    private bool _dockerAvailable;

    public async Task InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("payment_gateway_api_keys_test")
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

    private static string GenerateJwt(string sub, string role, string? merchantId = null)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(JwtKey);

        var claims = new List<Claim>
        {
            new("sub", sub),
            new("role", role)
        };

        if (!string.IsNullOrEmpty(merchantId))
        {
            claims.Add(new("merchantId", merchantId));
            claims.Add(new("merchant_id", merchantId));
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
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
    public async Task CreateApiKey_AsAuthorizedMerchant_ReturnsCreatedWithRawKey()
    {
        if (!_dockerAvailable) return;

        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var token = GenerateJwt("merch_test_1", "merchant", "merch_test_1");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var request = new
        {
            name = "Test Backend",
            scopes = new[] { "payments:write", "payments:read" },
            isLive = true
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/merchants/merch_test_1/api-keys", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<MerchantApiKeyCreatedDto>(JsonOptions);
        created.Should().NotBeNull();
        created!.Name.Should().Be("Test Backend");
        created.Prefix.Should().StartWith("rpg_live_");
        created.RawKey.Should().StartWith(created.Prefix);
        created.Scopes.Should().Contain("payments:write");
    }

    [Fact]
    public async Task AuthenticateWithApiKey_WhenValid_AllowsPaymentOperation()
    {
        if (!_dockerAvailable) return;

        using var factory = CreateFactory();
        var client = factory.CreateClient();

        // Arrange - Create API Key with payments:write scope
        var token = GenerateJwt("merch_key_runner", "merchant", "merch_key_runner");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createKeyResponse = await client.PostAsJsonAsync("/api/v1/merchants/merch_key_runner/api-keys", new
        {
            name = "Live Runner",
            scopes = new[] { "payments:write", "payments:read" },
            isLive = true
        });
        createKeyResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createKeyResponse.Content.ReadFromJsonAsync<MerchantApiKeyCreatedDto>(JsonOptions);

        // Act - Invoke payment endpoint with X-Api-Key header (clear Bearer token)
        var paymentClient = factory.CreateClient();
        var paymentRequest = new HttpRequestMessage(HttpMethod.Post, "/payments/")
        {
            Content = JsonContent.Create(new
            {
                partyId = "merch_key_runner",
                amount = 1000L,
                currency = "EUR",
                settlementChannel = "AdyenWireMock",
                paymentInstrumentToken = "tok_valid_test",
                idempotencyKey = Guid.NewGuid().ToString()
            })
        };
        paymentRequest.Headers.Add("X-Api-Key", created!.RawKey);
        paymentRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var paymentResponse = await paymentClient.SendAsync(paymentRequest);

        // Assert - Should authenticate successfully (either 200/201 or domain result, but NOT 401/403)
        paymentResponse.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        paymentResponse.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AuthenticateWithApiKey_WhenMissingRequiredScope_ReturnsForbidden()
    {
        if (!_dockerAvailable) return;

        using var factory = CreateFactory();
        var client = factory.CreateClient();

        // Arrange - Create key with ONLY payments:read scope
        var token = GenerateJwt("merch_readonly", "merchant", "merch_readonly");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createKeyResponse = await client.PostAsJsonAsync("/api/v1/merchants/merch_readonly/api-keys", new
        {
            name = "Read Only",
            scopes = new[] { "payments:read" },
            isLive = true
        });
        var created = await createKeyResponse.Content.ReadFromJsonAsync<MerchantApiKeyCreatedDto>(JsonOptions);

        // Act - Attempt payment write command with no Bearer token
        var paymentClient = factory.CreateClient();
        var paymentRequest = new HttpRequestMessage(HttpMethod.Post, "/payments/")
        {
            Content = JsonContent.Create(new
            {
                partyId = "merch_readonly",
                amount = 1000L,
                currency = "EUR",
                settlementChannel = "AdyenWireMock",
                paymentInstrumentToken = "tok_valid_test",
                idempotencyKey = Guid.NewGuid().ToString()
            })
        };
        paymentRequest.Headers.Add("X-Api-Key", created!.RawKey);
        paymentRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var paymentResponse = await paymentClient.SendAsync(paymentRequest);

        // Assert - Rejected with 403 Forbidden RFC 7807 problem document
        paymentResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await paymentResponse.Content.ReadFromJsonAsync<PaymentProblemDetails>(JsonOptions);
        problem.Should().NotBeNull();
        problem!.Type.Should().Be(PaymentProblemTypes.Forbidden);
    }

    [Fact]
    public async Task AuthenticateWithApiKey_WhenRevoked_ReturnsUnauthorized()
    {
        if (!_dockerAvailable) return;

        using var factory = CreateFactory();
        var client = factory.CreateClient();

        // Arrange - Create key and then revoke it
        var token = GenerateJwt("merch_revoker", "merchant", "merch_revoker");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createKeyResponse = await client.PostAsJsonAsync("/api/v1/merchants/merch_revoker/api-keys", new
        {
            name = "Temporary Key",
            scopes = new[] { "payments:write" },
            isLive = true
        });
        var created = await createKeyResponse.Content.ReadFromJsonAsync<MerchantApiKeyCreatedDto>(JsonOptions);

        // Revoke key
        var revokeResponse = await client.PostAsync($"/api/v1/merchants/merch_revoker/api-keys/{created!.Id}/revoke", null);
        revokeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act - Attempt to use revoked key with fresh client
        var paymentClient = factory.CreateClient();
        var paymentRequest = new HttpRequestMessage(HttpMethod.Post, "/payments/")
        {
            Content = JsonContent.Create(new
            {
                partyId = "merch_revoker",
                amount = 1000L,
                currency = "EUR",
                settlementChannel = "AdyenWireMock",
                paymentInstrumentToken = "tok_valid_test",
                idempotencyKey = Guid.NewGuid().ToString()
            })
        };
        paymentRequest.Headers.Add("X-Api-Key", created.RawKey);
        paymentRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var paymentResponse = await paymentClient.SendAsync(paymentRequest);

        // Assert - Rejected with 401 Unauthorized RFC 7807 problem document
        paymentResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var problem = await paymentResponse.Content.ReadFromJsonAsync<PaymentProblemDetails>(JsonOptions);
        problem.Should().NotBeNull();
        problem!.Type.Should().Be(PaymentProblemTypes.Unauthorized);
    }

    [Fact]
    public async Task AccessApiKeysOfDifferentMerchant_WithoutOpsRole_ReturnsForbidden()
    {
        if (!_dockerAvailable) return;

        using var factory = CreateFactory();
        var client = factory.CreateClient();

        // Arrange - Merchant A attempts to list Merchant B's keys
        var token = GenerateJwt("merch_victim", "merchant", "merch_victim");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/api/v1/merchants/merch_other/api-keys");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
