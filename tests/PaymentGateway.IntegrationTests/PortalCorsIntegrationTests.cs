using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;

namespace PaymentGateway.IntegrationTests;

public sealed class PortalCorsIntegrationTests : IAsyncLifetime
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
    public async Task OptionsPreflight_FromAllowedPortalOrigin_ReturnsCorsHeaders()
    {
        if (!_dockerAvailable)
        {
            return;
        }

        using var factory = CreateFactory();
        var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/payments/unresolved");
        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,x-correlation-id");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.NoContent, HttpStatusCode.OK);
        response.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowOriginValues).Should().BeTrue();
        allowOriginValues.Should().Contain("http://localhost:3000");
    }

    [Fact]
    public async Task OptionsPreflight_FromDisallowedOrigin_DoesNotReturnAllowedOriginHeader()
    {
        if (!_dockerAvailable)
        {
            return;
        }

        using var factory = CreateFactory();
        var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/payments/unresolved");
        request.Headers.Add("Origin", "http://malicious-site.example.com");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await client.SendAsync(request);

        response.Headers.TryGetValues("Access-Control-Allow-Origin", out _).Should().BeFalse();
    }

    private WebApplicationFactory<Program> CreateFactory()
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:PaymentDb"] = _postgres!.GetConnectionString(),
                    ["ConnectionStrings:RiskDb"] = _postgres.GetConnectionString(),
                    ["RabbitMq:UseInMemory"] = "true",
                    ["Auth:JwtSigningKey"] = "DEV_ONLY_CHANGE_ME_32CHARS_MINIMUM!!",
                    ["Cors:AllowedOrigins:0"] = "http://localhost:3000",
                    ["Cors:AllowedOrigins:1"] = "http://127.0.0.1:3000",
                    ["Database:AutoMigrate"] = "true"
                });
            });
        });
    }
}
