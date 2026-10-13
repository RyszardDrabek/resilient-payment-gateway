using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PaymentGateway.Application.Webhooks;

namespace PaymentGateway.Infrastructure.Webhooks;

public sealed class MerchantWebhookSimulator(
    HttpClient httpClient,
    IMerchantWebhookSigner signer,
    ILogger<MerchantWebhookSimulator> logger,
    TimeProvider? timeProvider = null)
    : IMerchantWebhookSimulator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<TestMerchantWebhookDispatchResultDto> SimulateDispatchAsync(
        string endpointUrl,
        string secretKey,
        string? eventType = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretKey);

        var eventId = "evt_sim_" + Guid.NewGuid().ToString("N")[..8];
        var paymentId = "pay_sim_" + Guid.NewGuid().ToString("N")[..8];
        var state = string.IsNullOrWhiteSpace(eventType) ? "payment.settled" : eventType;
        var occurredAt = _timeProvider.GetUtcNow().UtcDateTime;

        var payload = new MerchantWebhookPayload(
            EventId: eventId,
            PaymentId: paymentId,
            State: state,
            Amount: 10000L,
            Currency: "EUR",
            OccurredAt: occurredAt);

        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        var signature = signer.ComputeSignature(payloadJson, secretKey);
        var timestamp = _timeProvider.GetUtcNow().ToUnixTimeSeconds().ToString();

        var requestHeaders = new Dictionary<string, string>
        {
            ["X-Signature-SHA256"] = signature,
            ["X-Event-Id"] = eventId,
            ["X-Timestamp"] = timestamp,
            ["Content-Type"] = "application/json"
        };

        if (Uri.TryCreate(endpointUrl, UriKind.Absolute, out var parsedUri) && IsBlockedSsrfHost(parsedUri.Host))
        {
            return new TestMerchantWebhookDispatchResultDto(
                Success: false,
                StatusCode: 400,
                LatencyMs: 0,
                EndpointUrl: endpointUrl,
                EventId: eventId,
                Timestamp: timestamp,
                Signature: signature,
                RequestHeaders: requestHeaders,
                RequestPayload: payloadJson,
                ResponseBody: null,
                ErrorMessage: "Blocked request to cloud metadata or link-local address (SSRF protection).");
        }

        var stopwatch = Stopwatch.StartNew();
        int? statusCode = null;
        string? responseBody = null;
        string? errorMessage = null;
        var success = false;

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            using var request = new HttpRequestMessage(HttpMethod.Post, endpointUrl)
            {
                Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
            };

            request.Headers.Add("X-Signature-SHA256", signature);
            request.Headers.Add("X-Event-Id", eventId);
            request.Headers.Add("X-Timestamp", timestamp);

            using var response = await httpClient.SendAsync(request, linkedCts.Token);
            stopwatch.Stop();

            statusCode = (int)response.StatusCode;
            success = response.IsSuccessStatusCode;

            var body = await response.Content.ReadAsStringAsync(linkedCts.Token);
            responseBody = body.Length > 2000 ? body[..2000] + "... (truncated)" : body;

            if (!success)
            {
                errorMessage = $"HTTP {statusCode} {response.ReasonPhrase}";
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            stopwatch.Stop();
            errorMessage = "Request timed out after 5 seconds.";
            logger.LogWarning("Webhook simulation to {Url} timed out.", endpointUrl);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            errorMessage = ex.Message;
            logger.LogWarning(ex, "Webhook simulation to {Url} failed with exception: {Message}", endpointUrl, ex.Message);
        }

        return new TestMerchantWebhookDispatchResultDto(
            Success: success,
            StatusCode: statusCode,
            LatencyMs: stopwatch.ElapsedMilliseconds,
            EndpointUrl: endpointUrl,
            EventId: eventId,
            Timestamp: timestamp,
            Signature: signature,
            RequestHeaders: requestHeaders,
            RequestPayload: payloadJson,
            ResponseBody: responseBody,
            ErrorMessage: errorMessage);
    }

    private static bool IsBlockedSsrfHost(string host)
    {
        return string.Equals(host, "169.254.169.254", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(host, "metadata.google.internal", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(host, "instance-data", StringComparison.OrdinalIgnoreCase);
    }
}
