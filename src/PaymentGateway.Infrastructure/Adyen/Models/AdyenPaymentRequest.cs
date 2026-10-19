using System.Text.Json.Serialization;

namespace PaymentGateway.Infrastructure.Adyen.Models;

public sealed record AdyenPaymentRequest(
    [property: JsonPropertyName("amount")] AdyenAmount Amount,
    [property: JsonPropertyName("merchantAccount")] string MerchantAccount,
    [property: JsonPropertyName("reference")] string Reference,
    [property: JsonPropertyName("paymentMethod")] AdyenPaymentMethod? PaymentMethod = null,
    [property: JsonPropertyName("storePaymentMethod")] bool? StorePaymentMethod = null,
    [property: JsonPropertyName("shopperInteraction")] string? ShopperInteraction = null,
    [property: JsonPropertyName("recurringProcessingModel")] string? RecurringProcessingModel = null,
    [property: JsonPropertyName("shopperReference")] string? ShopperReference = null);
