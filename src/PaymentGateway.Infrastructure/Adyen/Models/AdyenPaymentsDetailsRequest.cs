using System.Text.Json.Serialization;

namespace PaymentGateway.Infrastructure.Adyen.Models;

public sealed record AdyenPaymentsDetailsRequest(
    [property: JsonPropertyName("details")] IReadOnlyDictionary<string, string>? Details = null,
    [property: JsonPropertyName("paymentData")] string? PaymentData = null,
    [property: JsonPropertyName("threeDSResult")] string? ThreeDSResult = null);
