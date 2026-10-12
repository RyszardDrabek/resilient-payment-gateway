namespace PaymentGateway.Application.Webhooks;

public interface IMerchantWebhookSigner
{
    string ComputeSignature(string payload, string secretKey);
}
