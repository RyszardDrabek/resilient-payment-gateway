using System.Text.Json.Serialization;

namespace PaymentGateway.Infrastructure.Adyen.Models;

public sealed record AdyenPaymentResponse(
    [property: JsonPropertyName("pspReference")] string PspReference,
    [property: JsonPropertyName("resultCode")] string ResultCode,
    [property: JsonPropertyName("refusalReason")] string? RefusalReason = null,
    [property: JsonPropertyName("merchantReference")] string? MerchantReference = null);
