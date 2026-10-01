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
            var url = BuildEndpointUrl("/v71/payments");
            using var response = await _httpClient.PostAsJsonAsync(url, paymentRequest, ct);

            if (!response.IsSuccessStatusCode)
            {
                return SettlementResult.Unanswered(
                    resolvedChannel,
                    $"Adyen simulator returned status code {(int)response.StatusCode}",
                    merchantReference);
            }

            var adyenResponse = await response.Content.ReadFromJsonAsync<AdyenPaymentResponse>(cancellationToken: ct);
            if (adyenResponse is null)
            {
                return SettlementResult.Unanswered(
                    resolvedChannel,
                    "Empty or null response payload received from Adyen simulator",
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
            _logger?.LogWarning(ex, "Settlement call to Adyen simulator was unanswered or timed out.");
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
        var captureRef = $"cap_{Guid.NewGuid():N}";
        var request = new AdyenModificationRequest(
            _options.MerchantAccount,
            captureRef,
            new AdyenAmount(currency, amount));

        return await SendModificationAsync(
            $"/v71/payments/{channelReference}/captures",
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
        var refundRef = $"ref_{Guid.NewGuid():N}";
        var request = new AdyenModificationRequest(
            _options.MerchantAccount,
            refundRef,
            new AdyenAmount(currency, amount));

        return await SendModificationAsync(
            $"/v71/payments/{channelReference}/refunds",
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
        var cancelRef = $"cnc_{Guid.NewGuid():N}";
        var request = new AdyenModificationRequest(
            _options.MerchantAccount,
            cancelRef);

        return await SendModificationAsync(
            $"/v71/payments/{channelReference}/cancels",
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
        try
        {
            var url = BuildEndpointUrl(relativePath);
            using var response = await _httpClient.PostAsJsonAsync(url, request, ct);

            if (!response.IsSuccessStatusCode)
            {
                return SettlementResult.Unanswered(
                    resolvedChannel,
                    $"Adyen simulator returned status code {(int)response.StatusCode}",
                    operationRef);
            }

            var modResponse = await response.Content.ReadFromJsonAsync<AdyenModificationResponse>(cancellationToken: ct);
            if (modResponse is null)
            {
                return SettlementResult.Unanswered(
                    resolvedChannel,
                    "Empty or null response payload received from Adyen simulator",
                    operationRef);
            }

            return SettlementResult.Success(
                resolvedChannel,
                modResponse.PspReference,
                operationRef);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or TaskCanceledException)
        {
            _logger?.LogWarning(ex, "Modification call to Adyen simulator was unanswered or timed out.");
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
            : (_mockServer?.Url ?? "http://localhost:8089");

        return baseUrl.TrimEnd('/') + relativePath;
    }
}
