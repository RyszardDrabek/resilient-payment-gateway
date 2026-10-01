using System.Text.Json.Serialization;

namespace PaymentGateway.Infrastructure.Adyen.Models;

public sealed record AdyenPaymentMethod(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("number")] string? Number = null,
    [property: JsonPropertyName("expiryMonth")] string? ExpiryMonth = null,
    [property: JsonPropertyName("expiryYear")] string? ExpiryYear = null,
    [property: JsonPropertyName("cvc")] string? Cvc = null,
    [property: JsonPropertyName("holderName")] string? HolderName = null);
