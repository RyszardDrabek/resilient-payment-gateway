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

public sealed class PaymentEndpointsTests : IAsyncLifetime
{
    private const string JwtKey = "DEV_ONLY_CHANGE_ME_32CHARS_MINIMUM!!";
    private PostgreSqlContainer? _postgres;
    private bool _dockerAvailable;

    public async Task InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("payment_gateway_test")
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

    private static string GenerateTestToken()
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(JwtKey);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([
                new Claim("sub", "merchant_test_1"),
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
    public async Task Post_Payments_Without_Token_Returns_401_Unauthorized()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var request = new AuthorizePaymentRequest("party_1", 1000, "EUR");
        var response = await client.PostAsJsonAsync("/payments", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Post_Payments_Without_IdempotencyKey_Returns_400_BadRequest()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateTestToken());

        var request = new AuthorizePaymentRequest("party_1", 1000, "EUR");
        var response = await client.PostAsJsonAsync("/payments", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("", 1000, "EUR")]       // Missing partyId
    [InlineData("   ", 1000, "EUR")]    // Whitespace partyId
    [InlineData("party_1", 0, "EUR")]   // Zero amount
    [InlineData("party_1", -50, "EUR")] // Negative amount
    [InlineData("party_1", 1000, "")]   // Missing currency
    [InlineData("party_1", 1000, "EU")] // Invalid 2-letter currency
    [InlineData("party_1", 1000, "EURO")]// Invalid 4-letter currency
    public async Task Post_Payments_With_Invalid_Input_Returns_400_BadRequest(string partyId, long amount, string currency)
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateTestToken());
        client.DefaultRequestHeaders.Add("Idempotency-Key", "idem_invalid_test");

        var invalidRequest = new AuthorizePaymentRequest(partyId, amount, currency);
        var response = await client.PostAsJsonAsync("/payments", invalidRequest);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Get_Payments_NonExistingId_Returns_404_NotFound()
    {
        if (!_dockerAvailable || _postgres is null)
        {
            return;
        }

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateTestToken());

        var response = await client.GetAsync("/payments/non_existent_pay_id");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Post_And_Get_Payment_Success_When_Database_Available()
    {
        if (!_dockerAvailable || _postgres is null)
        {
            return;
        }

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateTestToken());
        client.DefaultRequestHeaders.Add("Idempotency-Key", "idem_success_key_1");

        var request = new AuthorizePaymentRequest("party_success", 2500, "EUR");
        var postResponse = await client.PostAsJsonAsync("/payments", request);

        postResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await postResponse.Content.ReadFromJsonAsync<PaymentDto>();
        created.Should().NotBeNull();
        created!.PaymentId.Should().StartWith("pay_");
        created.State.Should().Be("Authorized");

        var getResponse = await client.GetAsync($"/payments/{created.PaymentId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await getResponse.Content.ReadFromJsonAsync<PaymentDto>();
        fetched.Should().NotBeNull();
        fetched!.PaymentId.Should().Be(created.PaymentId);
        fetched.PartyId.Should().Be("party_success");
    }

    [Fact]
    public async Task Post_Payments_DuplicateKey_SamePayload_Replays_Payment_Without_Duplication()
    {
        if (!_dockerAvailable || _postgres is null)
        {
            return;
        }

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateTestToken());
        client.DefaultRequestHeaders.Add("Idempotency-Key", "idem_replay_endpoint_key");

        var request = new AuthorizePaymentRequest("party_replay", 3000, "EUR");

        var firstResponse = await client.PostAsJsonAsync("/payments", request);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var firstResult = await firstResponse.Content.ReadFromJsonAsync<PaymentDto>();
        firstResult.Should().NotBeNull();

        var secondResponse = await client.PostAsJsonAsync("/payments", request);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var secondResult = await secondResponse.Content.ReadFromJsonAsync<PaymentDto>();
        secondResult.Should().NotBeNull();
        secondResult!.PaymentId.Should().Be(firstResult!.PaymentId);

        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        await using var db = new PaymentDbContext(options);
        var paymentCount = await db.Payments.CountAsync(p => p.PartyId == "party_replay");
        paymentCount.Should().Be(1);
    }

    [Fact]
    public async Task Post_Payments_DuplicateKey_DifferentPayload_Returns_409_Conflict()
    {
        if (!_dockerAvailable || _postgres is null)
        {
            return;
        }

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateTestToken());
        client.DefaultRequestHeaders.Add("Idempotency-Key", "idem_conflict_endpoint_key");

        var firstRequest = new AuthorizePaymentRequest("party_conflict", 1000, "EUR");
        var firstResponse = await client.PostAsJsonAsync("/payments", firstRequest);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var secondRequest = new AuthorizePaymentRequest("party_conflict", 2000, "EUR");
        var secondResponse = await client.PostAsJsonAsync("/payments", secondRequest);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Post_Payments_DuplicateKey_DifferentSettlementChannel_Returns_409_Conflict()
    {
        if (!_dockerAvailable || _postgres is null)
        {
            return;
        }

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateTestToken());
        client.DefaultRequestHeaders.Add("Idempotency-Key", "idem_conflict_channel_key");

        var firstRequest = new AuthorizePaymentRequest("party_channel", 1000, "EUR", SettlementChannel: "MOCK");
        var firstResponse = await client.PostAsJsonAsync("/payments", firstRequest);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var secondRequest = new AuthorizePaymentRequest("party_channel", 1000, "EUR", SettlementChannel: "ADYEN");
        var secondResponse = await client.PostAsJsonAsync("/payments", secondRequest);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ---- F-EDGE-01: RFC 7807 problem document body checks ----

    [Fact]
    public async Task Post_Payments_Without_IdempotencyKey_Returns400_WithProblemDetails()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateTestToken());

        var request = new AuthorizePaymentRequest("party_1", 1000, "EUR");
        var response = await client.PostAsJsonAsync("/payments", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var body = await response.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(body);
        doc.RootElement.GetProperty("type").GetString().Should().Be("urn:rpg:problem:validation");
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(400);
        doc.RootElement.GetProperty("correlationId").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Post_Payments_With_InvalidInput_Returns400_WithProblemDetails()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateTestToken());
        client.DefaultRequestHeaders.Add("Idempotency-Key", "idem_invalid_body_test");

        var request = new AuthorizePaymentRequest("", 0, "");
        var response = await client.PostAsJsonAsync("/payments", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var body = await response.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(body);
        doc.RootElement.GetProperty("type").GetString().Should().Be("urn:rpg:problem:validation");
    }

    [Fact]
    public async Task Get_Payments_NonExistingId_Returns404_WithProblemDetails()
    {
        if (!_dockerAvailable || _postgres is null)
        {
            return;
        }

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateTestToken());

        var response = await client.GetAsync("/payments/non_existent_pay_id_problem");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        response.Headers.Contains("X-Correlation-Id").Should().BeTrue();

        var body = await response.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(body);
        doc.RootElement.GetProperty("type").GetString().Should().Be("urn:rpg:problem:not-found");
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(404);
        doc.RootElement.GetProperty("correlationId").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Post_Payments_DuplicateKey_DifferentPayload_Returns409_WithProblemDetails()
    {
        if (!_dockerAvailable || _postgres is null)
        {
            return;
        }

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateTestToken());
        client.DefaultRequestHeaders.Add("Idempotency-Key", "idem_conflict_problem_body_key");

        var firstRequest = new AuthorizePaymentRequest("party_pb_conflict", 1000, "EUR");
        var firstResponse = await client.PostAsJsonAsync("/payments", firstRequest);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var secondRequest = new AuthorizePaymentRequest("party_pb_conflict", 9999, "EUR");
        var secondResponse = await client.PostAsJsonAsync("/payments", secondRequest);

        secondResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        secondResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var body = await secondResponse.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(body);
        doc.RootElement.GetProperty("type").GetString().Should().Be("urn:rpg:problem:conflict");
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(409);
    }
}
