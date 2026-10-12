using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.Events;
using PaymentGateway.Application.Webhooks;
using PaymentGateway.Domain.Ports;
using PaymentGateway.Infrastructure.Options;

namespace PaymentGateway.Infrastructure.Webhooks;

public sealed class MerchantWebhookDispatcher(
    IMerchantWebhookSubscriptionRepository repository,
    HttpClient httpClient,
    IMerchantWebhookSigner signer,
    IOptions<MerchantWebhookOptions> options,
    ILogger<MerchantWebhookDispatcher> logger,
    TimeProvider? timeProvider = null)
    : IMerchantWebhookDispatcher
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<MerchantWebhookDispatchResult> DispatchAsync(
        PaymentLifecycleEvent lifecycleEvent,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(lifecycleEvent);

        // 1. Resolve destination and signing key (DB subscription or options fallback)
        var subscription = await repository.GetActiveByPartyIdAsync(lifecycleEvent.PartyId, ct);
        var endpointUrl = subscription?.EndpointUrl ?? options.Value.DefaultEndpointUrl;
        var secretKey = subscription?.SecretKey ?? options.Value.DefaultSecretKey;

        if (string.IsNullOrWhiteSpace(endpointUrl) || string.IsNullOrWhiteSpace(secretKey))
        {
            logger.LogInformation(
                "No webhook endpoint or secret key configured for party {PartyId}. Skipping webhook dispatch.",
                lifecycleEvent.PartyId);
            return new MerchantWebhookDispatchResult(
                Success: false,
                EndpointUrl: string.Empty,
                PayloadJson: string.Empty,
                DeliveryAttempts: 0,
                ErrorMessage: "No active webhook subscription or fallback configuration.");
        }

        // 2. Build payload and compute signature
        var payload = new MerchantWebhookPayload(
            EventId: lifecycleEvent.EventId,
            PaymentId: lifecycleEvent.PaymentId,
            State: lifecycleEvent.Outcome,
            Amount: lifecycleEvent.Amount,
            Currency: lifecycleEvent.Currency,
            OccurredAt: lifecycleEvent.OccurredAt);

        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        var signature = signer.ComputeSignature(payloadJson, secretKey);
        var timestamp = _timeProvider.GetUtcNow().ToUnixTimeSeconds().ToString();

        // 3. Retry delivery with exponential backoff (up to MaxRetryAttempts)
        var maxAttempts = Math.Max(1, options.Value.MaxRetryAttempts);
        var initialBackoff = TimeSpan.FromSeconds(Math.Max(0, options.Value.InitialBackoffSeconds));
        var timeout = TimeSpan.FromSeconds(Math.Max(1, options.Value.TimeoutSeconds));

        string? lastError = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                using var attemptCts = new CancellationTokenSource(timeout);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, attemptCts.Token);

                using var request = new HttpRequestMessage(HttpMethod.Post, endpointUrl)
                {
                    Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
                };

                request.Headers.Add("X-Signature-SHA256", signature);
                request.Headers.Add("X-Event-Id", lifecycleEvent.EventId);
                request.Headers.Add("X-Timestamp", timestamp);

                logger.LogInformation(
                    "Dispatching webhook for event {EventId} to {Url} (attempt {Attempt}/{MaxAttempts})",
                    lifecycleEvent.EventId, endpointUrl, attempt, maxAttempts);

                using var response = await httpClient.SendAsync(request, linkedCts.Token);

                if (response.IsSuccessStatusCode)
                {
                    logger.LogInformation(
                        "Webhook delivered successfully for event {EventId} (status {StatusCode}, attempt {Attempt})",
                        lifecycleEvent.EventId, (int)response.StatusCode, attempt);

                    return new MerchantWebhookDispatchResult(
                        Success: true,
                        EndpointUrl: endpointUrl,
                        PayloadJson: payloadJson,
                        DeliveryAttempts: attempt);
                }

                lastError = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
                logger.LogWarning(
                    "Webhook delivery attempt {Attempt}/{MaxAttempts} returned failure status: {Error}",
                    attempt, maxAttempts, lastError);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
            {
                if (ct.IsCancellationRequested)
                {
                    throw;
                }

                lastError = ex.Message;
                logger.LogWarning(
                    ex,
                    "Webhook delivery attempt {Attempt}/{MaxAttempts} failed with exception: {Message}",
                    attempt, maxAttempts, ex.Message);
            }

            if (attempt < maxAttempts)
            {
                var delay = initialBackoff > TimeSpan.Zero
                    ? TimeSpan.FromMilliseconds(initialBackoff.TotalMilliseconds * Math.Pow(2, attempt - 1))
                    : TimeSpan.Zero;

                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, _timeProvider, ct);
                }
            }
        }

        logger.LogError(
            "Webhook delivery exhausted all {MaxAttempts} attempts for event {EventId} to {Url}. Last error: {LastError}",
            maxAttempts, lifecycleEvent.EventId, endpointUrl, lastError);

        return new MerchantWebhookDispatchResult(
            Success: false,
            EndpointUrl: endpointUrl,
            PayloadJson: payloadJson,
            DeliveryAttempts: maxAttempts,
            ErrorMessage: lastError ?? "Delivery attempts exhausted.");
    }
}
