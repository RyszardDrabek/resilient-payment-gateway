using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGateway.Domain.Ports;
using PaymentGateway.Infrastructure.Adyen.Models;

namespace PaymentGateway.Infrastructure.Adyen;

public sealed class AdyenSettlementPort : ISettlementPort
{
    private readonly HttpClient _httpClient;
    private readonly AdyenOptions _options;
    private readonly AdyenWireMockServer? _mockServer;
    private readonly ILogger<AdyenSettlementPort>? _logger;

    public AdyenSettlementPort(
        HttpClient httpClient,
        IOptions<AdyenOptions> options,
        AdyenWireMockServer? mockServer = null,
        ILogger<AdyenSettlementPort>? logger = null)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _mockServer = mockServer;
        _logger = logger;
    }

    public async Task<SettlementResult> AuthorizeAsync(
        string partyId,
        long amount,
        string currency,
        string? channel = null,
        CancellationToken ct = default)
    {
        var resolvedChannel = !string.IsNullOrWhiteSpace(channel) ? channel : "ADYEN";
        var merchantReference = $"mref_{Guid.NewGuid():N}";

        if (_options.IsLiveMode && !_options.HasValidCredentials)
        {
            _logger?.LogError("Adyen live mode selected but credentials are absent or invalid. Refusing settlement.");
            return SettlementResult.ConfigurationFail(
                resolvedChannel,
                "Adyen live sandbox mode is selected but credentials are absent or invalid.",
                merchantReference);
        }

        // Configure request instrument fields (AC-4) and mock triggers
        var isDecline = !string.IsNullOrEmpty(partyId) && partyId.StartsWith("decline_", StringComparison.OrdinalIgnoreCase);
        var isTimeout = !string.IsNullOrEmpty(partyId) && partyId.Contains("timeout", StringComparison.OrdinalIgnoreCase);
        var isServerError = !string.IsNullOrEmpty(partyId) && partyId.Contains("server_error", StringComparison.OrdinalIgnoreCase);

        var cardNumber = isDecline ? "4111111111110002" : "4111111111111111";
        var refString = isTimeout ? $"{merchantReference}_timeout" : (isServerError ? $"{merchantReference}_server_error" : merchantReference);

        var paymentRequest = new AdyenPaymentRequest(
            new AdyenAmount(currency, amount),
            _options.MerchantAccount,
            refString,
            new AdyenPaymentMethod(
                Type: "scheme",
                Number: cardNumber,
                ExpiryMonth: "03",
                ExpiryYear: "2030",
                Cvc: "737",
                HolderName: "J. Doe"));

        try
        {
            var url = BuildEndpointUrl(AdyenEndpoints.Payments(_options.ApiVersion));
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(paymentRequest)
            };

            if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                httpRequest.Headers.TryAddWithoutValidation("X-API-Key", _options.ApiKey);
            }

            using var response = await _httpClient.SendAsync(httpRequest, ct);

            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            {
                _logger?.LogError("Adyen sandbox returned authentication error {StatusCode}.", (int)response.StatusCode);
                return SettlementResult.ConfigurationFail(
                    resolvedChannel,
                    $"Adyen live credentials rejected by sandbox: {(int)response.StatusCode}",
                    merchantReference);
            }

            if (!response.IsSuccessStatusCode)
            {
                return SettlementResult.Unanswered(
                    resolvedChannel,
                    $"Adyen settlement returned status code {(int)response.StatusCode}",
                    merchantReference);
            }

            var adyenResponse = await response.Content.ReadFromJsonAsync<AdyenPaymentResponse>(cancellationToken: ct);
            if (adyenResponse is null)
            {
                return SettlementResult.Unanswered(
                    resolvedChannel,
                    "Empty or null response payload received from Adyen settlement",
                    merchantReference);
            }

            if (string.Equals(adyenResponse.ResultCode, "Authorised", StringComparison.OrdinalIgnoreCase))
            {
                return SettlementResult.Success(
                    resolvedChannel,
                    adyenResponse.PspReference,
                    merchantReference);
            }

            return SettlementResult.Declined(
                resolvedChannel,
                adyenResponse.PspReference,
                adyenResponse.RefusalReason ?? "Refused",
                merchantReference);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or TaskCanceledException)
        {
            _logger?.LogWarning(ex, "Settlement call to Adyen was unanswered or timed out.");
            return SettlementResult.Unanswered(
                resolvedChannel,
                $"Settlement channel failed to answer: {ex.Message}",
                merchantReference);
        }
    }

    public async Task<SettlementResult> CaptureAsync(
        string paymentId,
        string channelReference,
        long amount,
        string currency,
        string? channel = null,
        CancellationToken ct = default)
    {
        var resolvedChannel = !string.IsNullOrWhiteSpace(channel) ? channel : "ADYEN";
        var captureRef = string.IsNullOrWhiteSpace(paymentId) ? $"cap_{Guid.NewGuid():N}" : $"{paymentId}_cap_{Guid.NewGuid():N}";
        var request = new AdyenModificationRequest(
            _options.MerchantAccount,
            captureRef,
            new AdyenAmount(currency, amount));

        return await SendModificationAsync(
            AdyenEndpoints.Captures(channelReference, _options.ApiVersion),
            request,
            resolvedChannel,
            captureRef,
            ct);
    }

    public async Task<SettlementResult> RefundAsync(
        string paymentId,
        string channelReference,
        long amount,
        string currency,
        string? channel = null,
        CancellationToken ct = default)
    {
        var resolvedChannel = !string.IsNullOrWhiteSpace(channel) ? channel : "ADYEN";
        var refundRef = string.IsNullOrWhiteSpace(paymentId) ? $"ref_{Guid.NewGuid():N}" : $"{paymentId}_ref_{Guid.NewGuid():N}";
        var request = new AdyenModificationRequest(
            _options.MerchantAccount,
            refundRef,
            new AdyenAmount(currency, amount));

        return await SendModificationAsync(
            AdyenEndpoints.Refunds(channelReference, _options.ApiVersion),
            request,
            resolvedChannel,
            refundRef,
            ct);
    }

    public async Task<SettlementResult> CancelAsync(
        string paymentId,
        string channelReference,
        string? channel = null,
        CancellationToken ct = default)
    {
        var resolvedChannel = !string.IsNullOrWhiteSpace(channel) ? channel : "ADYEN";
        var cancelRef = string.IsNullOrWhiteSpace(paymentId) ? $"cnc_{Guid.NewGuid():N}" : $"{paymentId}_cnc_{Guid.NewGuid():N}";
        var request = new AdyenModificationRequest(
            _options.MerchantAccount,
            cancelRef);

        return await SendModificationAsync(
            AdyenEndpoints.Cancels(channelReference, _options.ApiVersion),
            request,
            resolvedChannel,
            cancelRef,
            ct);
    }

    private async Task<SettlementResult> SendModificationAsync(
        string relativePath,
        AdyenModificationRequest request,
        string resolvedChannel,
        string operationRef,
        CancellationToken ct)
    {
        if (_options.IsLiveMode && !_options.HasValidCredentials)
        {
            _logger?.LogError("Adyen live mode selected but credentials are absent or invalid. Refusing modification.");
            return SettlementResult.ConfigurationFail(
                resolvedChannel,
                "Adyen live sandbox mode is selected but credentials are absent or invalid.",
                operationRef);
        }

        try
        {
            var url = BuildEndpointUrl(relativePath);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(request)
            };

            if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                httpRequest.Headers.TryAddWithoutValidation("X-API-Key", _options.ApiKey);
            }

            using var response = await _httpClient.SendAsync(httpRequest, ct);

            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            {
                _logger?.LogError("Adyen sandbox returned authentication error {StatusCode} for modification.", (int)response.StatusCode);
                return SettlementResult.ConfigurationFail(
                    resolvedChannel,
                    $"Adyen live credentials rejected by sandbox: {(int)response.StatusCode}",
                    operationRef);
            }

            if (!response.IsSuccessStatusCode)
            {
                return SettlementResult.Unanswered(
                    resolvedChannel,
                    $"Adyen settlement returned status code {(int)response.StatusCode}",
                    operationRef);
            }

            var modResponse = await response.Content.ReadFromJsonAsync<AdyenModificationResponse>(cancellationToken: ct);
            if (modResponse is null)
            {
                return SettlementResult.Unanswered(
                    resolvedChannel,
                    "Empty or null response payload received from Adyen settlement",
                    operationRef);
            }

            return SettlementResult.Success(
                resolvedChannel,
                modResponse.PspReference,
                operationRef);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or TaskCanceledException)
        {
            _logger?.LogWarning(ex, "Modification call to Adyen was unanswered or timed out.");
            return SettlementResult.Unanswered(
                resolvedChannel,
                $"Settlement channel failed to answer: {ex.Message}",
                operationRef);
        }
    }

    private string BuildEndpointUrl(string relativePath)
    {
        var baseUrl = !string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? _options.BaseUrl
            : (_options.IsLiveMode ? AdyenEndpoints.LiveSandboxBaseUrl : (_mockServer?.Url ?? "http://localhost:8089"));

        return baseUrl.TrimEnd('/') + relativePath;
    }
}
