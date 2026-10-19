using System.Text.Json.Serialization;

namespace PaymentGateway.Infrastructure.Adyen.Models;

public sealed record AdyenAction(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("url")] string? Url = null,
    [property: JsonPropertyName("paymentData")] string? PaymentData = null,
    [property: JsonPropertyName("token")] string? Token = null);

public sealed record AdyenPaymentResponse(
    [property: JsonPropertyName("pspReference")] string PspReference,
    [property: JsonPropertyName("resultCode")] string ResultCode,
    [property: JsonPropertyName("refusalReason")] string? RefusalReason = null,
    [property: JsonPropertyName("merchantReference")] string? MerchantReference = null,
    [property: JsonPropertyName("action")] AdyenAction? Action = null,
    [property: JsonPropertyName("storedPaymentMethodId")] string? StoredPaymentMethodId = null);
