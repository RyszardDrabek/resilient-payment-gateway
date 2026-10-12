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
using PaymentGateway.Application.Payments;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace PaymentGateway.IntegrationTests;

public sealed class UnresolvedPaymentEndpointsIntegrationTests : IAsyncLifetime
{
    private const string JwtKey = "DEV_ONLY_CHANGE_ME_32CHARS_MINIMUM!!";
    private PostgreSqlContainer? _postgres;
    private bool _dockerAvailable;

    public async Task InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("payment_gateway_unresolved_test")
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

    private static string GenerateTokenWithRole(string role)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(JwtKey);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([
                new Claim("sub", "operator_test_user"),
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

    private async Task SeedPaymentsAsync(DateTimeOffset baseTime)
    {
        if (!_dockerAvailable || _postgres is null) return;

        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        await using var db = new PaymentDbContext(options);
        await db.Payments.ExecuteDeleteAsync();

        var pOldPendingAdyen = new Payment(
            id: "pay_old_pending_adyen",
            partyId: "party_1",
            amount: 1000,
            currency: "EUR",
            settlementChannel: "ADYEN",
            state: PaymentState.Pending,
            channelReference: "psp_old_pending");
        SetPaymentCreatedAt(pOldPendingAdyen, baseTime.AddMinutes(-20));

        var pOldUnknownWeb3 = new Payment(
            id: "pay_old_unknown_web3",
            partyId: "party_2",
            amount: 2000,
            currency: "EUR",
            settlementChannel: "WEB3",
            state: PaymentState.Unknown,
            channelReference: "tx_old_unknown");
        SetPaymentCreatedAt(pOldUnknownWeb3, baseTime.AddMinutes(-25));

        var pRecentPendingAdyen = new Payment(
            id: "pay_recent_pending_adyen",
            partyId: "party_3",
            amount: 3000,
            currency: "EUR",
            settlementChannel: "ADYEN",
            state: PaymentState.Pending,
            channelReference: "psp_recent");
        SetPaymentCreatedAt(pRecentPendingAdyen, baseTime.AddMinutes(-5));

        var pAuthorized = new Payment(
            id: "pay_authorized",
            partyId: "party_4",
            amount: 4000,
            currency: "EUR",
            settlementChannel: "ADYEN",
            state: PaymentState.Authorized,
            channelReference: "psp_auth");
        SetPaymentCreatedAt(pAuthorized, baseTime.AddMinutes(-30));

        var pCaptured = new Payment(
            id: "pay_captured",
            partyId: "party_5",
            amount: 5000,
            currency: "EUR",
            settlementChannel: "ADYEN",
            state: PaymentState.Captured,
            channelReference: "psp_cap");
        SetPaymentCreatedAt(pCaptured, baseTime.AddMinutes(-40));

        db.Payments.AddRange(pOldPendingAdyen, pOldUnknownWeb3, pRecentPendingAdyen, pAuthorized, pCaptured);
        await db.SaveChangesAsync();
    }

    private static void SetPaymentCreatedAt(Payment payment, DateTimeOffset createdAt)
    {
        var prop = typeof(Payment).GetProperty(nameof(Payment.CreatedAt));
        prop?.SetValue(payment, createdAt);
    }

    [Fact]
    public async Task AC1_QueriesUnresolvedPayments_ReturnsPendingAndUnknown_OlderThanAgeThreshold()
    {
        if (!_dockerAvailable || _postgres is null) return;

        var baseTime = DateTimeOffset.UtcNow;
        await SeedPaymentsAsync(baseTime);

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateTokenWithRole("merchant"));

        var response = await client.GetAsync("/api/v1/payments/unresolved");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<UnresolvedPaymentsResponse>();
        content.Should().NotBeNull();
        content!.AgeMinutes.Should().Be(15);
        content.TotalCount.Should().Be(2);

        var ids = content.Items.Select(x => x.PaymentId).ToList();
        ids.Should().Contain("pay_old_pending_adyen");
        ids.Should().Contain("pay_old_unknown_web3");
        ids.Should().NotContain("pay_recent_pending_adyen");
        ids.Should().NotContain("pay_authorized");
        ids.Should().NotContain("pay_captured");
    }

    [Fact]
    public async Task AC2_WithChannelFilter_NarrowsStrictlyToSettlementChannel()
    {
        if (!_dockerAvailable || _postgres is null) return;

        var baseTime = DateTimeOffset.UtcNow;
        await SeedPaymentsAsync(baseTime);

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateTokenWithRole("ops"));

        var adyenResponse = await client.GetAsync("/api/v1/payments/unresolved?channel=adyen");
        adyenResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var adyenContent = await adyenResponse.Content.ReadFromJsonAsync<UnresolvedPaymentsResponse>();
        adyenContent!.TotalCount.Should().Be(1);
        adyenContent.Items.Should().ContainSingle(x => x.PaymentId == "pay_old_pending_adyen");

        var web3Response = await client.GetAsync("/api/v1/payments/unresolved?channel=web3");
        web3Response.StatusCode.Should().Be(HttpStatusCode.OK);
        var web3Content = await web3Response.Content.ReadFromJsonAsync<UnresolvedPaymentsResponse>();
        web3Content!.TotalCount.Should().Be(1);
        web3Content.Items.Should().ContainSingle(x => x.PaymentId == "pay_old_unknown_web3");
    }

    [Fact]
    public async Task AC3_ExposesRequiredSummaryFields()
    {
        if (!_dockerAvailable || _postgres is null) return;

        var baseTime = DateTimeOffset.UtcNow;
        await SeedPaymentsAsync(baseTime);

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateTokenWithRole("risk"));

        var response = await client.GetAsync("/api/v1/payments/unresolved");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<UnresolvedPaymentsResponse>();
        content.Should().NotBeNull();
        content!.Page.Should().Be(1);
        content.PageSize.Should().Be(50);
        content.TotalCount.Should().Be(2);

        var first = content.Items.First(x => x.PaymentId == "pay_old_pending_adyen");
        first.PaymentId.Should().Be("pay_old_pending_adyen");
        first.State.Should().Be("Pending");
        first.SettlementChannel.Should().Be("ADYEN");
        first.ChannelReference.Should().Be("psp_old_pending");
        first.DurationElapsedMinutes.Should().BeGreaterThanOrEqualTo(15);
    }

    [Fact]
    public async Task AC4_LacksBearerToken_Returns401_ProblemDocument()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/payments/unresolved");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("type").GetString().Should().Be("urn:rpg:problem:unauthorized");
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(401);
    }

    [Fact]
    public async Task AC4_LacksAuthorizedRole_Returns403_ProblemDocument()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateTokenWithRole("customer"));

        var response = await client.GetAsync("/api/v1/payments/unresolved");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("type").GetString().Should().Be("urn:rpg:problem:forbidden");
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(403);
    }

    [Theory]
    [InlineData("merchant")]
    [InlineData("risk")]
    [InlineData("ops")]
    public async Task AC4_AuthorizedRoles_Allowed(string role)
    {
        if (!_dockerAvailable || _postgres is null) return;

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateTokenWithRole(role));

        var response = await client.GetAsync("/api/v1/payments/unresolved");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task NegativeAgeMinutes_Returns400_ValidationProblemDetails()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateTokenWithRole("merchant"));

        var response = await client.GetAsync("/api/v1/payments/unresolved?ageMinutes=-5");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("type").GetString().Should().Be("urn:rpg:problem:validation");
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(400);
    }
}
