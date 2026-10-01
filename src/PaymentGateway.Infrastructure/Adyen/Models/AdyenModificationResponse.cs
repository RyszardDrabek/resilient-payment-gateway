using System.Text.Json.Serialization;

namespace PaymentGateway.Infrastructure.Adyen.Models;

public sealed record AdyenModificationResponse(
    [property: JsonPropertyName("pspReference")] string PspReference,
    [property: JsonPropertyName("paymentPspReference")] string? PaymentPspReference = null,
    [property: JsonPropertyName("status")] string Status = "received",
    [property: JsonPropertyName("reference")] string? Reference = null);
