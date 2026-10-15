using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using PaymentGateway.Application.Metrics;
using Testcontainers.PostgreSql;
using Xunit;

namespace PaymentGateway.IntegrationTests;

public sealed class PrometheusMetricsIntegrationTests : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private bool _dockerAvailable;

    public async Task InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("payment_gateway_metrics")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await _postgres.StartAsync();
            _dockerAvailable = true;
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

    [Fact]
    public async Task GetMetrics_AnonymousAccess_ReturnsPrometheusTextPayloadWithCustomMeters()
    {

        var connectionString = _dockerAvailable && _postgres is not null
            ? _postgres.GetConnectionString()
            : "Host=localhost;Port=5432;Database=test;Username=postgres;Password=postgres";

        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:PaymentDb"] = connectionString,
                        ["ConnectionStrings:RiskDb"] = connectionString,
                        ["RabbitMq:UseInMemory"] = "true",
                        ["Auth:JwtSigningKey"] = "DEV_ONLY_CHANGE_ME_32CHARS_MINIMUM!!",
                        ["Database:AutoMigrate"] = "false"
                    });
                });
            });

        // 1. Start the host by creating the client first, so OpenTelemetry's MeterListener is active
        var client = factory.CreateClient();

        // 2. Trigger metrics after the host and OpenTelemetry MeterListener have started
        PaymentMetrics.RecordVolume(2500, "EUR", "ADYEN", "authorize", "Authorized");
        PaymentMetrics.RecordChannelDuration(0.085, "ADYEN", "authorize", "Authorized");
        PaymentMetrics.RecordWebhookDispatch("delivered", 200);
        PaymentMetrics.RecordWebhookReplay("success", 200);
        PaymentMetrics.RecordWebhookDuration(0.04, "delivered");

        // 3. Scrape Prometheus endpoint
        var response = await client.GetAsync("/metrics");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("payment_volume_total");
        body.Should().Contain("payment_channel_duration_seconds");
        body.Should().Contain("merchant_webhook_dispatched_total");
        body.Should().Contain("merchant_webhook_replay_attempts_total");
        body.Should().Contain("merchant_webhook_dispatch_duration_seconds");
    }
}
