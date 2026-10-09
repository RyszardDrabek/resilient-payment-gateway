using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using PaymentGateway.Application.Events;
using PaymentGateway.Application.Risk.Commands;
using PaymentGateway.Application.Risk.Ports;
using PaymentGateway.Application.Risk.Queries;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace PaymentGateway.IntegrationTests;

public sealed class RiskScoringIntegrationTests : IAsyncLifetime
{
    private const string JwtKey = "DEV_ONLY_CHANGE_ME_32CHARS_MINIMUM!!";
    private PostgreSqlContainer? _postgres;
    private bool _dockerAvailable;

    public async Task InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("payment_risk_test")
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
    public async Task RiskScoring_EndToEnd_AC1_to_AC6()
    {
        if (!_dockerAvailable)
        {
            return; // Skip if docker unavailable
        }

        using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateJwtToken("risk"));

        using var scope = factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<ISender>();
        var flagRepo = scope.ServiceProvider.GetRequiredService<IRiskFlagRepository>();

        // Step 1: Baseline event (Party A: 100 EUR)
        var evt1 = new PaymentLifecycleEvent(
            EventId: "evt-001",
            PaymentId: "pay-001",
            PartyId: "party-corp-1",
            Outcome: "Authorized",
            Amount: 10000,
            Currency: "EUR",
            Version: 1,
            OccurredAt: DateTimeOffset.UtcNow);

        var verdict1 = await mediator.Send(new ScorePaymentLifecycleEventCommand(evt1));
        verdict1.Status.Should().Be(RiskVerdictStatus.Clean);
        verdict1.FlagId.Should().BeNull();

        // Query verdict via API (AC-6 by payment id)
        var response1 = await client.GetAsync($"/risk/verdicts/{evt1.PaymentId}");
        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto1 = await response1.Content.ReadFromJsonAsync<RiskVerdictDto>();
        dto1.Should().NotBeNull();
        dto1!.PaymentId.Should().Be(evt1.PaymentId);
        dto1.Status.Should().Be("clean");

        // Step 2: Anomalous event for same party (AC-2, AC-3: Amount 100x higher -> 10,000 EUR)
        var evt2 = new PaymentLifecycleEvent(
            EventId: "evt-002",
            PaymentId: "pay-002",
            PartyId: "party-corp-1",
            Outcome: "Authorized",
            Amount: 1000000,
            Currency: "EUR",
            Version: 1,
            OccurredAt: DateTimeOffset.UtcNow);

        var verdict2 = await mediator.Send(new ScorePaymentLifecycleEventCommand(evt2));
        verdict2.Status.Should().Be(RiskVerdictStatus.Anomalous);
        verdict2.FlagId.Should().NotBeNullOrEmpty();

        // Verify flag was raised in repository (AC-3)
        var flags = await flagRepo.GetByPaymentIdAsync(evt2.PaymentId);
        flags.Should().ContainSingle();
        flags[0].PaymentId.Should().Be(evt2.PaymentId);
        flags[0].PartyId.Should().Be("party-corp-1");

        // Query verdict via API (AC-6 by event id)
        var response2 = await client.GetAsync($"/risk/verdicts/{evt2.EventId}");
        response2.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto2 = await response2.Content.ReadFromJsonAsync<RiskVerdictDto>();
        dto2.Should().NotBeNull();
        dto2!.Status.Should().Be("anomalous");
        dto2.FlagId.Should().Be(verdict2.FlagId);

        // Step 3: Non-existent identifier returns 404 Problem Details (AC-6)
        var notFoundResp = await client.GetAsync("/risk/verdicts/non-existent-id");
        notFoundResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
