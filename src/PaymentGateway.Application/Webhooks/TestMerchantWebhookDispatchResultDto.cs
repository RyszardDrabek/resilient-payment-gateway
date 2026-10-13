namespace PaymentGateway.Application.Webhooks;

public sealed record TestMerchantWebhookDispatchResultDto(
    bool Success,
    int? StatusCode,
    long LatencyMs,
    string EndpointUrl,
    string EventId,
    string Timestamp,
    string Signature,
    Dictionary<string, string> RequestHeaders,
    string RequestPayload,
    string? ResponseBody,
    string? ErrorMessage);
