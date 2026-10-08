using System.Text.Json.Serialization;

namespace PaymentGateway.Application.Adyen;

public sealed record AdyenWebhookPayload(
    [property: JsonPropertyName("live")]
    string? Live,
    [property: JsonPropertyName("notificationItems")]
    IReadOnlyList<AdyenNotificationItemWrapper> NotificationItems);
