namespace PaymentGateway.Application.Adyen;

public sealed record AdyenWebhookIngestionResult(
    bool IsSuccess,
    bool IsReplay = false,
    bool IsInvalidHmac = false,
    string? ErrorMessage = null)
{
    public static AdyenWebhookIngestionResult Success() =>
        new(true);

    public static AdyenWebhookIngestionResult Replay(string? message = null) =>
        new(false, IsReplay: true, ErrorMessage: message ?? "Adyen notification replay detected.");

    public static AdyenWebhookIngestionResult InvalidHmac(string? message = null) =>
        new(false, IsInvalidHmac: true, ErrorMessage: message ?? "Adyen HMAC verification failed.");

    public static AdyenWebhookIngestionResult Failure(string message) =>
        new(false, ErrorMessage: message);
}
