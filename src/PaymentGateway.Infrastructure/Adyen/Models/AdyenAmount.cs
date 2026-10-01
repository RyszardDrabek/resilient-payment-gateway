using System.Text.Json.Serialization;

namespace PaymentGateway.Infrastructure.Adyen.Models;

public sealed record AdyenAmount(
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("value")] long Value);
