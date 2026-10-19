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

                // Stored token simulation
                string? storedToken = null;
                if (body.Contains("\"storePaymentMethod\":true", StringComparison.OrdinalIgnoreCase) ||
                    body.Contains("\"storePaymentMethod\": true", StringComparison.OrdinalIgnoreCase))
                {
                    storedToken = $"adyen_tok_{Guid.NewGuid():N}";
                }

                // MIT Recurring simulation (ContAuth)
                if (body.Contains("ContAuth", StringComparison.OrdinalIgnoreCase))
                {
                    if (body.Contains("invalid", StringComparison.OrdinalIgnoreCase))
                    {
                        var refuseResponse = new AdyenPaymentResponse(
                            pspRef,
                            "Refused",
                            "InvalidCard",
                            merchantRef);
                        return CreateJsonResponse(200, refuseResponse);
                    }

                    if (body.Contains("expired", StringComparison.OrdinalIgnoreCase))
                    {
                        var refuseResponse = new AdyenPaymentResponse(
                            pspRef,
                            "Refused",
                            "ExpiredCard",
                            merchantRef);
                        return CreateJsonResponse(200, refuseResponse);
                    }

                    var mitSuccessResponse = new AdyenPaymentResponse(
                        pspRef,
                        "Authorised",
                        null,
                        merchantRef,
                        StoredPaymentMethodId: storedToken);
                    return CreateJsonResponse(200, mitSuccessResponse);
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
                    body.Contains("0003", StringComparison.OrdinalIgnoreCase))
                {
                    var declineResponse = new AdyenPaymentResponse(
                        pspRef,
                        "Refused",
                        "Refused",
                        merchantRef);

                    return CreateJsonResponse(200, declineResponse);
                }

                // 3DS2 Challenge simulation (card suffix 0002 or challenge keyword)
                if (body.Contains("0002", StringComparison.OrdinalIgnoreCase) ||
                    body.Contains("challenge", StringComparison.OrdinalIgnoreCase))
                {
                    var challengeResponse = new AdyenPaymentResponse(
                        pspRef,
                        "ChallengeShopper",
                        null,
                        merchantRef,
                        new AdyenAction(
                            "threeDS2",
                            "https://checkoutshopper-test.adyen.com/checkoutshopper/threeDS2.shtml",
                            $"mock_payment_data_{Guid.NewGuid():N}",
                            $"mock_token_{Guid.NewGuid():N}"));

                    return CreateJsonResponse(200, challengeResponse);
                }

                // Success simulation
                var successResponse = new AdyenPaymentResponse(
                    pspRef,
                    "Authorised",
                    null,
                    merchantRef,
                    StoredPaymentMethodId: storedToken);

                return CreateJsonResponse(200, successResponse);
            }));

        // 2. 3DS2 Details: POST /{version}/payments/details
        var detailsPath = AdyenEndpoints.PaymentsDetails(version);
        server
            .Given(Request.Create().WithPath(detailsPath).UsingPost())
            .RespondWith(Response.Create().WithCallback(requestMessage =>
            {
                var body = requestMessage.Body ?? string.Empty;
                var pspRef = $"adyen_3ds_auth_{Guid.NewGuid():N}";

                if (body.Contains("refused", StringComparison.OrdinalIgnoreCase) ||
                    body.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
                    body.Contains("0003", StringComparison.OrdinalIgnoreCase))
                {
                    var declineResponse = new AdyenPaymentResponse(
                        pspRef,
                        "Refused",
                        "ThreeDsRefused",
                        "3ds_challenge_failed");

                    return CreateJsonResponse(200, declineResponse);
                }

                var successResponse = new AdyenPaymentResponse(
                    pspRef,
                    "Authorised",
                    null,
                    "3ds_challenge_success");

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

        // 5. Query status: GET /{version}/payments/{paymentPspReference}
        server
            .Given(Request.Create().WithPath(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !p.EndsWith("/captures", StringComparison.OrdinalIgnoreCase) && !p.EndsWith("/refunds", StringComparison.OrdinalIgnoreCase) && !p.EndsWith("/cancels", StringComparison.OrdinalIgnoreCase)).UsingGet())
            .RespondWith(Response.Create().WithCallback(requestMessage =>
            {
                var path = requestMessage.Path;
                var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var paymentPsp = segments.Length >= 3 ? segments[2] : "unknown_psp";

                if (paymentPsp.Contains("unresolved", StringComparison.OrdinalIgnoreCase))
                {
                    return CreateJsonResponse(404, new { status = 404, message = "Payment not found or still unresolved" });
                }

                if (paymentPsp.Contains("decline", StringComparison.OrdinalIgnoreCase) || paymentPsp.Contains("refused", StringComparison.OrdinalIgnoreCase))
                {
                    var declineResponse = new AdyenPaymentResponse(
                        paymentPsp,
                        "Refused",
                        "Refused by acquirer",
                        paymentPsp);

                    return CreateJsonResponse(200, declineResponse);
                }

                var successResponse = new AdyenPaymentResponse(
                    paymentPsp,
                    "Authorised",
                    null,
                    paymentPsp);

                return CreateJsonResponse(200, successResponse);
            }));
    }
}

