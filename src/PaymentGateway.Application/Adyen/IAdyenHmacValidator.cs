namespace PaymentGateway.Application.Adyen;

public interface IAdyenHmacValidator
{
    bool Validate(AdyenNotificationRequestItem item, string? hmacKeyOverride = null);
    string CalculateSignature(AdyenNotificationRequestItem item, string? hmacKeyOverride = null);
}
