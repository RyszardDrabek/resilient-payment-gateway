using System.Security.Cryptography;
using System.Text;

namespace PaymentGateway.Application.Webhooks;

public sealed class HmacSha256WebhookSigner : IMerchantWebhookSigner
{
    public string ComputeSignature(string payload, string secretKey)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretKey);

        var keyBytes = Encoding.UTF8.GetBytes(secretKey);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);

        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(payloadBytes);

        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
