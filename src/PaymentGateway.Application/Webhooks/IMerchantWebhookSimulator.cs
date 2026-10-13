namespace PaymentGateway.Application.Webhooks;

public interface IMerchantWebhookSimulator
{
    Task<TestMerchantWebhookDispatchResultDto> SimulateDispatchAsync(
        string endpointUrl,
        string secretKey,
        string? eventType = null,
        CancellationToken ct = default);
}
