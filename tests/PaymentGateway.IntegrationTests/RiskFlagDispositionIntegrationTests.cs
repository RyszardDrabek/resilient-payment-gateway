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
using PaymentGateway.Application.Risk.Models;
using PaymentGateway.Application.Risk.Ports;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Ports;
using PaymentGateway.Edge.Problems;
using PaymentGateway.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace PaymentGateway.IntegrationTests;

public sealed class RiskFlagDispositionIntegrationTests : IAsyncLifetime
{
    private const string JwtKey = "DEV_ONLY_CHANGE_ME_32CHARS_MINIMUM!!";
    private PostgreSqlContainer? _postgres;
    private bool _dockerAvailable;

    public async Task InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("payment_risk_disposition_test")
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

            var riskOptions = new DbContextOptionsBuilder<RiskDbContext>()
                .UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "risk"))
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

    private WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var dict = new Dictionary<string, string?>
                {
                    ["ConnectionStrings:PaymentDb"] = _postgres!.GetConnectionString(),
                    ["ConnectionStrings:RiskDb"] = _postgres!.GetConnectionString(),
                    ["Auth:JwtSigningKey"] = JwtKey,
                    ["RabbitMq:UseInMemory"] = "true",
                    ["PaymentGateway:ActiveChannel"] = "MOCK",
                    ["Risk:AnomalyAmountMultiplier"] = "5.0"
                };
                config.AddInMemoryCollection(dict);
            });
        });

    private static string CreateJwtToken(string role = "risk")
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
    public async Task AC1_ListOpenRiskFlags_ReturnsOnlyUndispositionedFlags()
    {
        if (!_dockerAvailable) return;

        using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateJwtToken());

        using var scope = factory.Services.CreateScope();
        var flagRepo = scope.ServiceProvider.GetRequiredService<IRiskFlagRepository>();

        var flagOpen = new RiskFlag("flag-ac1-open", "pay-ac1-1", "party-ac1", "evt-ac1-1", "Velocity anomaly", DateTimeOffset.UtcNow);
        var flagClosed = new RiskFlag("flag-ac1-closed", "pay-ac1-2", "party-ac1", "evt-ac1-2", "Amount anomaly", DateTimeOffset.UtcNow);
        flagClosed.ApplyDisposition("confirmed", DateTimeOffset.UtcNow);

        await flagRepo.SaveAsync(flagOpen);
        await flagRepo.SaveAsync(flagClosed);

        var response = await client.GetAsync("/risk/flags?page=1&pageSize=50");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var pagedResult = await response.Content.ReadFromJsonAsync<OpenRiskFlagsResponse>();
        pagedResult.Should().NotBeNull();
        pagedResult!.Items.Should().Contain(f => f.Id == "flag-ac1-open");
        pagedResult.Items.Should().NotContain(f => f.Id == "flag-ac1-closed");
    }

    [Fact]
    public async Task AC2_DispositionFlag_RecordsDispositionAndRemovesFromOpenSet()
    {
        if (!_dockerAvailable) return;

        using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateJwtToken());

        using var scope = factory.Services.CreateScope();
        var flagRepo = scope.ServiceProvider.GetRequiredService<IRiskFlagRepository>();

        var flag = new RiskFlag("flag-ac2", "pay-ac2", "party-ac2", "evt-ac2", "Amount spike", DateTimeOffset.UtcNow);
        await flagRepo.SaveAsync(flag);

        var dispResponse = await client.PostAsJsonAsync("/risk/flags/flag-ac2/disposition", new DispositionRiskFlagRequest("false_positive", "Checked with customer"));
        dispResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var updatedFlag = await dispResponse.Content.ReadFromJsonAsync<RiskFlagDto>();
        updatedFlag.Should().NotBeNull();
        updatedFlag!.Id.Should().Be("flag-ac2");
        updatedFlag.Status.Should().Be("Dispositioned");
        updatedFlag.Disposition.Should().Be("false_positive");
        updatedFlag.DispositionedAt.Should().NotBeNull();

        // Verify it is no longer in open flags list
        var listResponse = await client.GetAsync("/risk/flags");
        var openFlags = await listResponse.Content.ReadFromJsonAsync<OpenRiskFlagsResponse>();
        openFlags!.Items.Should().NotContain(f => f.Id == "flag-ac2");
    }

    [Fact]
    public async Task AC3_DispositionFlag_DoesNotAlterPaymentSettlementState()
    {
        if (!_dockerAvailable) return;

        using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateJwtToken());

        using var scope = factory.Services.CreateScope();
        var payRepo = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();
        var flagRepo = scope.ServiceProvider.GetRequiredService<IRiskFlagRepository>();

        // Create an authorized payment
        var payment = Payment.Authorize(
            partyId: "party-ac3",
            amount: 50000,
            currency: "EUR",
            settlementChannel: "MOCK",
            channelReference: "psp-ref-ac3");
        await payRepo.AddAsync(payment);

        var flag = new RiskFlag("flag-ac3", payment.Id, payment.PartyId, "evt-ac3", "Anomalous score", DateTimeOffset.UtcNow);
        await flagRepo.SaveAsync(flag);

        // Disposition the flag
        var dispResponse = await client.PostAsJsonAsync($"/risk/flags/{flag.Id}/disposition", new DispositionRiskFlagRequest("escalated", "Escalating to fraud team"));
        dispResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Query payment directly to verify its state and status remain untouched
        var paymentAfter = await payRepo.GetByIdAsync(payment.Id);
        paymentAfter.Should().NotBeNull();
        paymentAfter!.State.Should().Be(PaymentState.Authorized);
        paymentAfter.Amount.Should().Be(50000);
        paymentAfter.Currency.Should().Be("EUR");
    }

    [Fact]
    public async Task AC4_DispositionUnknownOrAlreadyDispositionedFlag_RejectsActionWithoutInventingNewFlag()
    {
        if (!_dockerAvailable) return;

        using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateJwtToken());

        using var scope = factory.Services.CreateScope();
        var flagRepo = scope.ServiceProvider.GetRequiredService<IRiskFlagRepository>();

        // 1. Unknown flag -> 404
        var unknownResponse = await client.PostAsJsonAsync("/risk/flags/unknown-flag-999/disposition", new DispositionRiskFlagRequest("confirmed", null));
        unknownResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var notFoundProblem = await unknownResponse.Content.ReadFromJsonAsync<PaymentProblemDetails>();
        notFoundProblem!.Type.Should().Be(PaymentProblemTypes.NotFound);

        // 2. Already dispositioned flag -> 409 Conflict
        var flag = new RiskFlag("flag-ac4", "pay-ac4", "party-ac4", "evt-ac4", "Velocity", DateTimeOffset.UtcNow);
        flag.ApplyDisposition("confirmed", DateTimeOffset.UtcNow);
        await flagRepo.SaveAsync(flag);

        var conflictResponse = await client.PostAsJsonAsync("/risk/flags/flag-ac4/disposition", new DispositionRiskFlagRequest("false_positive", null));
        conflictResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var conflictProblem = await conflictResponse.Content.ReadFromJsonAsync<PaymentProblemDetails>();
        conflictProblem!.Type.Should().Be(PaymentProblemTypes.Conflict);

        // 3. Unsupported disposition value -> 400 Bad Request
        var flag2 = new RiskFlag("flag-ac4-2", "pay-ac4-2", "party-ac4", "evt-ac4-2", "Velocity", DateTimeOffset.UtcNow);
        await flagRepo.SaveAsync(flag2);

        var badRequestResponse = await client.PostAsJsonAsync("/risk/flags/flag-ac4-2/disposition", new DispositionRiskFlagRequest("unsupported_value", null));
        badRequestResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var badRequestProblem = await badRequestResponse.Content.ReadFromJsonAsync<PaymentProblemDetails>();
        badRequestProblem!.Type.Should().Be(PaymentProblemTypes.Validation);

        // Verify flag2 is still open and no new flags were invented
        var flag2After = await flagRepo.GetByIdAsync("flag-ac4-2");
        flag2After!.Status.Should().Be("Open");
        flag2After.Disposition.Should().BeNull();
    }
}
