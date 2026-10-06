using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGateway.Infrastructure.Adyen.Models;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using WireMock.Settings;

namespace PaymentGateway.Infrastructure.Adyen;

public sealed class AdyenWireMockServer : IHostedService, IDisposable
{
    private readonly AdyenOptions _options;
    private readonly ILogger<AdyenWireMockServer>? _logger;
    private WireMockServer? _server;

    public AdyenWireMockServer(IOptions<AdyenOptions> options, ILogger<AdyenWireMockServer>? logger = null)
    {
        _options = options.Value;
        _logger = logger;
    }

    public string? Url => _server?.Url;
    public int Port => _server?.Port ?? 0;
    public bool IsRunning => _server?.IsStarted ?? false;

    public void Start()
    {
        if (_server is not null && _server.IsStarted)
        {
            return;
        }

        var settings = new WireMockServerSettings
        {
            Port = _options.MockServerPort > 0 ? _options.MockServerPort : null
        };

        _server = WireMockServer.Start(settings);
        SetupStubs(_server);
        _logger?.LogInformation("Adyen WireMock server started at {Url}", _server.Url);
    }

    private readonly object _syncLock = new();

    public void Stop()
    {
        lock (_syncLock)
        {
            var server = _server;
            _server = null;

            if (server is null)
            {
                return;
            }

            try
            {
                if (server.IsStarted)
                {
                    server.Stop();
                }
                server.Dispose();
                _logger?.LogInformation("Adyen WireMock server stopped");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error while stopping Adyen WireMock server");
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_options.AutoStartMockServer)
        {
            Start();
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Stop();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        Stop();
    }

    private static WireMock.ResponseMessage CreateJsonResponse(int statusCode, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        return new WireMock.ResponseMessage
        {
            StatusCode = statusCode,
            Headers = new Dictionary<string, WireMock.Types.WireMockList<string>>
            {
                ["Content-Type"] = "application/json"
            },
            BodyData = new WireMock.Util.BodyData
            {
                DetectedBodyType = WireMock.Types.BodyType.String,
                BodyAsString = json,
                Encoding = System.Text.Encoding.UTF8
            }
        };
    }

    private void SetupStubs(WireMockServer server)
    {
        var version = _options.ApiVersion;
        var paymentsPath = AdyenEndpoints.Payments(version);
        var prefix = AdyenEndpoints.PaymentsPathPrefix(version);

        // 1. Authorize: POST /{version}/payments
        server
            .Given(Request.Create().WithPath(paymentsPath).UsingPost())
            .RespondWith(Response.Create().WithCallback(async requestMessage =>
            {
                var body = requestMessage.Body ?? string.Empty;
                var pspRef = $"adyen_auth_{Guid.NewGuid():N}";

                string merchantRef = "unknown_ref";
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("reference", out var refProp))
                    {
                        merchantRef = refProp.GetString() ?? merchantRef;
                    }
                }
                catch
                {
                    // Ignore JSON parsing failures in stub
                }

                // Timeout simulation
                if (body.Contains("timeout", StringComparison.OrdinalIgnoreCase))
                {
                    await Task.Delay(TimeSpan.FromSeconds(10));
                }

                // Error / 500 simulation
                if (body.Contains("server_error", StringComparison.OrdinalIgnoreCase))
                {
                    return CreateJsonResponse(500, new { status = 500, message = "Adyen simulator internal error" });
                }

                // Decline simulation
                if (body.Contains("decline", StringComparison.OrdinalIgnoreCase) ||
                    body.Contains("0002", StringComparison.OrdinalIgnoreCase))
                {
                    var declineResponse = new AdyenPaymentResponse(
                        pspRef,
                        "Refused",
                        "Refused",
                        merchantRef);

                    return CreateJsonResponse(200, declineResponse);
                }

                // Success simulation
                var successResponse = new AdyenPaymentResponse(
                    pspRef,
                    "Authorised",
                    null,
                    merchantRef);

                return CreateJsonResponse(200, successResponse);
            }));

        // 2. Capture: POST /{version}/payments/{paymentPspReference}/captures
        server
            .Given(Request.Create().WithPath(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && p.EndsWith("/captures", StringComparison.OrdinalIgnoreCase)).UsingPost())
            .RespondWith(Response.Create().WithCallback(requestMessage =>
            {
                var path = requestMessage.Path;
                var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var paymentPsp = segments.Length >= 3 ? segments[2] : "unknown_psp";

                string refVal = "cap_ref";
                try
                {
                    using var doc = JsonDocument.Parse(requestMessage.Body ?? "{}");
                    if (doc.RootElement.TryGetProperty("reference", out var refProp))
                    {
                        refVal = refProp.GetString() ?? refVal;
                    }
                }
                catch { }

                var capResponse = new AdyenModificationResponse(
                    $"adyen_cap_{Guid.NewGuid():N}",
                    paymentPsp,
                    "received",
                    refVal);

                return CreateJsonResponse(200, capResponse);
            }));

        // 3. Refund: POST /{version}/payments/{paymentPspReference}/refunds
        server
            .Given(Request.Create().WithPath(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && p.EndsWith("/refunds", StringComparison.OrdinalIgnoreCase)).UsingPost())
            .RespondWith(Response.Create().WithCallback(requestMessage =>
            {
                var path = requestMessage.Path;
                var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var paymentPsp = segments.Length >= 3 ? segments[2] : "unknown_psp";

                string refVal = "ref_ref";
                try
                {
                    using var doc = JsonDocument.Parse(requestMessage.Body ?? "{}");
                    if (doc.RootElement.TryGetProperty("reference", out var refProp))
                    {
                        refVal = refProp.GetString() ?? refVal;
                    }
                }
                catch { }

                var refResponse = new AdyenModificationResponse(
                    $"adyen_ref_{Guid.NewGuid():N}",
                    paymentPsp,
                    "received",
                    refVal);

                return CreateJsonResponse(200, refResponse);
            }));

        // 4. Cancel: POST /{version}/payments/{paymentPspReference}/cancels
        server
            .Given(Request.Create().WithPath(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && p.EndsWith("/cancels", StringComparison.OrdinalIgnoreCase)).UsingPost())
            .RespondWith(Response.Create().WithCallback(requestMessage =>
            {
                var path = requestMessage.Path;
                var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var paymentPsp = segments.Length >= 3 ? segments[2] : "unknown_psp";

                string refVal = "cnc_ref";
                try
                {
                    using var doc = JsonDocument.Parse(requestMessage.Body ?? "{}");
                    if (doc.RootElement.TryGetProperty("reference", out var refProp))
                    {
                        refVal = refProp.GetString() ?? refVal;
                    }
                }
                catch { }

                var cncResponse = new AdyenModificationResponse(
                    $"adyen_cnc_{Guid.NewGuid():N}",
                    paymentPsp,
                    "received",
                    refVal);

                return CreateJsonResponse(200, cncResponse);
            }));
    }
}
