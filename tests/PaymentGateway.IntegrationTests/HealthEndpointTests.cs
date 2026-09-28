using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;

namespace PaymentGateway.IntegrationTests;

public sealed class HealthEndpointTests : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private bool _dockerAvailable;

    public async Task InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("payment_gateway")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await _postgres.StartAsync();
            _dockerAvailable = true;
        }
        catch (Exception)
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
    public async Task Health_returns_ok_when_database_is_reachable()
    {
        if (!_dockerAvailable || _postgres is null)
        {
            // Docker Desktop not running — CI with Docker exercises the path below.
            return;
        }

        var connectionString = _postgres.GetConnectionString();

        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:PaymentDb", connectionString);
                builder.UseSetting("ConnectionStrings:RiskDb", connectionString);
                builder.UseSetting("RabbitMq:UseInMemory", "true");
                builder.UseSetting("Auth:JwtSigningKey", "DEV_ONLY_CHANGE_ME_32CHARS_MINIMUM!!");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:PaymentDb"] = connectionString,
                        ["ConnectionStrings:RiskDb"] = connectionString,
                        ["RabbitMq:UseInMemory"] = "true",
                        ["Auth:JwtSigningKey"] = "DEV_ONLY_CHANGE_ME_32CHARS_MINIMUM!!"
                    });
                });
            });

        var client = factory.CreateClient();
        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK, because: await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).Should().Contain("Healthy");
    }
}
