using System.Text.Json.Serialization;

namespace PaymentGateway.Application.Adyen;

public sealed record AdyenNotificationRequestItem(
    [property: JsonPropertyName("additionalData")]
    Dictionary<string, string>? AdditionalData,
    [property: JsonPropertyName("amount")]
    AdyenAmountDto? Amount,
    [property: JsonPropertyName("eventCode")]
    string EventCode,
    [property: JsonPropertyName("eventDate")]
    DateTimeOffset? EventDate,
    [property: JsonPropertyName("merchantAccountCode")]
    string MerchantAccountCode,
    [property: JsonPropertyName("merchantReference")]
    string MerchantReference,
    [property: JsonPropertyName("originalReference")]
    string? OriginalReference,
    [property: JsonPropertyName("pspReference")]
    string PspReference,
    [property: JsonPropertyName("reason")]
    string? Reason,
    [property: JsonPropertyName("success")]
    string Success);
