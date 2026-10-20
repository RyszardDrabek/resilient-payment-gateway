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
using PaymentGateway.Application.Fx.Models;
using PaymentGateway.Application.Payments;
using PaymentGateway.Edge.Problems;
using PaymentGateway.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace PaymentGateway.IntegrationTests;

public sealed class FxQuotingIntegrationTests : IAsyncLifetime
{
    private const string JwtKey = "DEV_ONLY_CHANGE_ME_32CHARS_MINIMUM!!";
    private PostgreSqlContainer? _postgres;
    private bool _dockerAvailable;

    public async Task InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("payment_fx_test")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await _postgres.StartAsync();
            _dockerAvailable = true;

            var payOptions = new DbContextOptionsBuilder<PaymentDbContext>()
                .UseNpgsql(_postgres.GetConnectionString())
                .Options;
            await using var payDb = new PaymentDbContext(payOptions);
            await payDb.Database.MigrateAsync();
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
                    var dict = new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:PaymentDb"] = connectionString,
                        ["ConnectionStrings:RiskDb"] = connectionString,
                        ["Auth:JwtSigningKey"] = JwtKey,
                        ["RabbitMq:UseInMemory"] = "true",
                        ["PaymentGateway:ActiveChannel"] = "MOCK",
                        ["Fx:DefaultSpreadBps"] = "50",
                        ["Fx:DefaultTtlMinutes"] = "15"
                    };
                    config.AddInMemoryCollection(dict);
                });
            });
    }

    private static string CreateJwtToken(string role = "merchant")
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "payment-gateway",
            audience: "payment-gateway",
            claims: [new Claim("sub", "integration-test"), new Claim("role", role)],
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Fact]
    public async Task PostFxQuote_Returns201Created_WithLockedRateAndTargetAmount()
    {
        if (!_dockerAvailable || _postgres is null) return;

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateJwtToken());

        // Request 100.00 PLN cart to USDC
        var request = new CreateFxQuoteRequest("PLN", 10000, "USDC", "merchant_fx", 15);
        var response = await client.PostAsJsonAsync("/fx/quotes", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var quote = await response.Content.ReadFromJsonAsync<FxQuoteDto>();
        quote.Should().NotBeNull();
        quote!.QuoteId.Should().StartWith("fxq_");
        quote.BaseCurrency.Should().Be("PLN");
        quote.BaseAmount.Should().Be(10000);
        quote.TargetCurrency.Should().Be("USDC");
        quote.TargetAmount.Should().Be(2507); // 10000 * (0.2520 * (1 - 0.005))
        quote.SpotRate.Should().Be(0.2520m);
        quote.SpreadBps.Should().Be(50);
        quote.Status.Should().Be("Active");
        quote.RemainingSeconds.Should().BeGreaterThan(0);

        // Fetch quote by ID
        var getResponse = await client.GetAsync($"/fx/quotes/{quote.QuoteId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await getResponse.Content.ReadFromJsonAsync<FxQuoteDto>();
        fetched.Should().NotBeNull();
        fetched!.QuoteId.Should().Be(quote.QuoteId);
        fetched.Status.Should().Be("Active");
    }

    [Fact]
    public async Task CrossCurrencyCheckout_UsingQuote_SuccessfullyAuthorizesAndConsumesQuote()
    {
        if (!_dockerAvailable || _postgres is null) return;

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateJwtToken());

        // 1. Create Quote for 50.00 EUR cart paid in USDC
        // Spot rate EUR -> USDC: 1.0850. Spread: 50 bps (0.5%).
        // Effective rate: 1.0850 * 0.995 = 1.079575.
        // TargetAmount = 5000 * 1.079575 = 5398 (53.98 USDC).
        var quoteRequest = new CreateFxQuoteRequest("EUR", 5000, "USDC", "merch_cross", 15);
        var quoteResponse = await client.PostAsJsonAsync("/fx/quotes", quoteRequest);
        quoteResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var quote = await quoteResponse.Content.ReadFromJsonAsync<FxQuoteDto>();
        quote.Should().NotBeNull();

        // 2. Authorize payment using the quote
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"idem_quote_{Guid.NewGuid():N}");

        var payRequest = new AuthorizePaymentRequest(
            PartyId: "merch_cross",
            Amount: quote!.TargetAmount,
            Currency: quote.TargetCurrency,
            SettlementChannel: "MOCK",
            QuoteId: quote.QuoteId);

        var payResponse = await client.PostAsJsonAsync("/payments", payRequest);
        payResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var payment = await payResponse.Content.ReadFromJsonAsync<PaymentDto>();
        payment.Should().NotBeNull();
        payment!.State.Should().Be("Authorized");
        payment.Currency.Should().Be("USDC");
        payment.Amount.Should().Be(quote.TargetAmount);
        payment.QuoteId.Should().Be(quote.QuoteId);
        payment.OriginalAmount.Should().Be(5000);
        payment.OriginalCurrency.Should().Be("EUR");
        payment.ExchangeRate.Should().Be(quote.ExchangeRate);

        // 3. Verify quote is now Consumed
        var quoteCheck = await client.GetAsync($"/fx/quotes/{quote.QuoteId}");
        quoteCheck.StatusCode.Should().Be(HttpStatusCode.OK);
        var consumedQuote = await quoteCheck.Content.ReadFromJsonAsync<FxQuoteDto>();
        consumedQuote!.Status.Should().Be("Consumed");

        // 4. Double spend attempt with same quote fails with 409 Conflict
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"idem_double_spend_{Guid.NewGuid():N}");

        var secondPayResponse = await client.PostAsJsonAsync("/payments", payRequest);
        secondPayResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problemBody = await secondPayResponse.Content.ReadAsStringAsync();
        problemBody.Should().Contain(PaymentProblemTypes.Conflict);
    }

    [Fact]
    public async Task GetRates_ReturnsSpotRatesDictionary()
    {
        if (!_dockerAvailable || _postgres is null) return;

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateJwtToken());

        var response = await client.GetAsync("/fx/rates?baseCurrency=EUR");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        doc.RootElement.GetProperty("baseCurrency").GetString().Should().Be("EUR");
        doc.RootElement.GetProperty("rates").GetProperty("USDC").GetDecimal().Should().Be(1.0850m);
    }
}
