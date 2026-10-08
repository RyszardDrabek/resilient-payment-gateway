using System.Text.Json.Serialization;

namespace PaymentGateway.Application.Adyen;

public sealed record AdyenNotificationItemWrapper(
    [property: JsonPropertyName("NotificationRequestItem")]
    AdyenNotificationRequestItem NotificationRequestItem);
