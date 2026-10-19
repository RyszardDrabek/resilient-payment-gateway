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
using PaymentGateway.Application.PaymentMethods;
using PaymentGateway.Application.Payments;
using PaymentGateway.Application.Subscriptions;
using PaymentGateway.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace PaymentGateway.IntegrationTests;

public sealed class RecurringAndSubscriptionsIntegrationTests : IAsyncLifetime
{
    private const string JwtKey = "DEV_ONLY_CHANGE_ME_32CHARS_MINIMUM!!";
    private PostgreSqlContainer? _postgres;
    private bool _dockerAvailable;

    public async Task InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("recurring_sub_test")
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

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:PaymentDb", connectionString);
            builder.UseSetting("ConnectionStrings:RiskDb", connectionString);
            builder.UseSetting("RabbitMq:UseInMemory", "true");
            builder.UseSetting("Auth:JwtSigningKey", JwtKey);

            builder.ConfigureAppConfiguration((_, config) =>
            {
                var dict = new Dictionary<string, string?>
                {
                    ["ConnectionStrings:PaymentDb"] = connectionString,
                    ["ConnectionStrings:RiskDb"] = connectionString,
                    ["Auth:JwtSigningKey"] = JwtKey,
                    ["Auth:Issuer"] = "payment-gateway",
                    ["Auth:Audience"] = "payment-gateway",
                    ["RabbitMq:UseInMemory"] = "true",
                    ["Adyen:IsLiveMode"] = "false",
                    ["Adyen:AutoStartMockServer"] = "true",
                    ["PaymentGateway:ActiveChannel"] = "ADYEN"
                };

                config.AddInMemoryCollection(dict);
            });
        });
    }

    private static string GenerateJwt(string partyId = "merch_acme")
    {
        var handler = new JwtSecurityTokenHandler();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey));
        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, partyId),
                new Claim("sub", partyId),
                new Claim("party_id", partyId),
                new Claim("merchant_id", partyId),
                new Claim("role", "merchant"),
                new Claim("scope", "payments:write payments:read")
            ]),
            Expires = DateTime.UtcNow.AddHours(1),
            Issuer = "payment-gateway",
            Audience = "payment-gateway",
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        });
        return handler.WriteToken(token);
    }

    [Fact]
    public async Task PaymentMethodsAndSubscriptions_FullLifecycle_Succeeds()
    {
        if (!_dockerAvailable)
        {
            return;
        }

        using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwt("merch_sub_test"));

        // 1. Tokenize Payment Method
        var tokenizeReq = new TokenizePaymentMethodRequest(
            "merch_sub_test",
            "ADYEN",
            "visa",
            "**** **** **** 1111",
            12,
            2030,
            "adyen_tok_test_999");

        var tokenResp = await client.PostAsJsonAsync("/api/v1/payment-methods", tokenizeReq);
        tokenResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdToken = await tokenResp.Content.ReadFromJsonAsync<PaymentMethodDto>();
        createdToken.Should().NotBeNull();
        createdToken!.Id.Should().StartWith("pm_");
        createdToken.Status.Should().Be("Active");

        // 2. List Payment Methods
        var listResp = await client.GetAsync("/api/v1/payment-methods?partyId=merch_sub_test");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var methods = await listResp.Content.ReadFromJsonAsync<List<PaymentMethodDto>>();
        methods.Should().ContainSingle(m => m.Id == createdToken.Id);

        // 3. Create Subscription
        var createSubReq = new CreateSubscriptionRequest(
            "merch_sub_test",
            createdToken.Id,
            2900,
            "EUR",
            "Monthly");

        var subResp = await client.PostAsJsonAsync("/api/v1/subscriptions", createSubReq);
        subResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdSub = await subResp.Content.ReadFromJsonAsync<SubscriptionDto>();
        createdSub.Should().NotBeNull();
        createdSub!.Id.Should().StartWith("sub_");
        createdSub.State.Should().Be("Active");
        createdSub.Amount.Should().Be(2900);

        // 4. Pause Subscription
        var pauseResp = await client.PostAsync($"/api/v1/subscriptions/{createdSub.Id}/pause", null);
        pauseResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var pausedSub = await pauseResp.Content.ReadFromJsonAsync<SubscriptionDto>();
        pausedSub!.State.Should().Be("Paused");

        // 5. Resume Subscription
        var resumeResp = await client.PostAsync($"/api/v1/subscriptions/{createdSub.Id}/resume", null);
        resumeResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var resumedSub = await resumeResp.Content.ReadFromJsonAsync<SubscriptionDto>();
        resumedSub!.State.Should().Be("Active");

        // 6. Execute MIT Payment referencing Stored Card
        var mitReq = new AuthorizePaymentRequest(
            "merch_sub_test",
            2900,
            "EUR",
            SettlementChannel: "ADYEN",
            IdempotencyKey: $"mit_test_{Guid.NewGuid():N}",
            TransactionOrigin: "MIT",
            PaymentMethodId: createdToken.Id,
            SubscriptionId: createdSub.Id);

        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", mitReq.IdempotencyKey);
        var payResp = await client.PostAsJsonAsync("/payments", mitReq);
        payResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var payDto = await payResp.Content.ReadFromJsonAsync<PaymentDto>();
        payDto.Should().NotBeNull();
        payDto!.State.Should().Be("Authorized");
        payDto.TransactionOrigin.Should().Be("MIT");
        payDto.PaymentMethodId.Should().Be(createdToken.Id);
        payDto.SubscriptionId.Should().Be(createdSub.Id);

        // 7. Cancel Subscription
        var cancelResp = await client.PostAsync($"/api/v1/subscriptions/{createdSub.Id}/cancel", null);
        cancelResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var cancelledSub = await cancelResp.Content.ReadFromJsonAsync<SubscriptionDto>();
        cancelledSub!.State.Should().Be("Cancelled");

        // 8. Revoke Payment Method
        var deleteResp = await client.DeleteAsync($"/api/v1/payment-methods/{createdToken.Id}");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listAfterRevoke = await client.GetFromJsonAsync<List<PaymentMethodDto>>("/api/v1/payment-methods?partyId=merch_sub_test&activeOnly=true");
        listAfterRevoke.Should().BeEmpty();
    }
}
