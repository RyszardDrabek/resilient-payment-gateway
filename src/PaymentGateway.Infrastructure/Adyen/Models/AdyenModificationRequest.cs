using System.Text.Json.Serialization;

namespace PaymentGateway.Infrastructure.Adyen.Models;

public sealed record AdyenModificationRequest(
    [property: JsonPropertyName("merchantAccount")] string MerchantAccount,
    [property: JsonPropertyName("reference")] string Reference,
    [property: JsonPropertyName("amount")] AdyenAmount? Amount = null);
