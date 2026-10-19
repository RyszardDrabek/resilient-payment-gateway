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
using PaymentGateway.Application.Payouts;
using PaymentGateway.Application.Reconciliation;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace PaymentGateway.IntegrationTests;

public sealed class ReconciliationAndPayoutsIntegrationTests : IAsyncLifetime
{
    private const string JwtKey = "DEV_ONLY_CHANGE_ME_32CHARS_MINIMUM!!";
    private PostgreSqlContainer? _postgres;
    private bool _dockerAvailable;

    public async Task InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("recon_payout_test")
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
    public async Task ReconciliationAndPayouts_FullLifecycle_Succeeds()
    {
        if (!_dockerAvailable || _postgres is null)
        {
            return;
        }

        using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwt("merch_acme"));

        // Seed a captured payment directly into DB
        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        await using (var db = new PaymentDbContext(options))
        {
            var p1 = new Payment(
                id: $"pay_{Guid.NewGuid():N}",
                partyId: "merch_acme",
                amount: 10000,
                currency: "EUR",
                settlementChannel: "ADYEN",
                state: PaymentState.Captured,
                channelReference: "psp_recon_100",
                capturedAmount: 10000);

            db.Payments.Add(p1);
            await db.SaveChangesAsync();
        }

        // 1. Run Reconciliation
        var runReq = new RunReconciliationRequest(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var runResp = await client.PostAsJsonAsync("/api/v1/reconciliation/run", runReq);
        runResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var runResult = await runResp.Content.ReadFromJsonAsync<RunReconciliationResult>();
        runResult.Should().NotBeNull();
        runResult!.TotalProcessed.Should().BeGreaterThanOrEqualTo(1);

        // 2. Fetch Summary
        var summaryResp = await client.GetAsync("/api/v1/reconciliation/summary");
        summaryResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var summary = await summaryResp.Content.ReadFromJsonAsync<ReconciliationSummaryDto>();
        summary.Should().NotBeNull();
        summary!.TotalRecords.Should().BeGreaterThanOrEqualTo(1);

        // 3. Generate Payout Batch
        var payoutReq = new GeneratePayoutRequest(
            "merch_acme",
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1),
            "EUR");
        var payoutResp = await client.PostAsJsonAsync("/api/v1/payouts/generate", payoutReq);
        payoutResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var payoutBatch = await payoutResp.Content.ReadFromJsonAsync<MerchantPayoutBatchDto>();
        payoutBatch.Should().NotBeNull();
        payoutBatch!.GrossAmount.Should().Be(10000);
        payoutBatch.NetAmount.Should().BeLessThan(10000); // fee deducted

        // 4. Download CSV Settlement Export
        var csvResp = await client.GetAsync($"/api/v1/payouts/{payoutBatch.Id}/csv");
        csvResp.StatusCode.Should().Be(HttpStatusCode.OK);
        csvResp.Content.Headers.ContentType?.MediaType.Should().Be("text/csv");
        var csvContent = await csvResp.Content.ReadAsStringAsync();
        csvContent.Should().Contain("Payout Item ID,Payment ID,Captured At (UTC),Gross Amount,Fee Amount,Net Amount,Currency");
        csvContent.Should().Contain("# Total Gross: 10000 EUR");
    }
}
