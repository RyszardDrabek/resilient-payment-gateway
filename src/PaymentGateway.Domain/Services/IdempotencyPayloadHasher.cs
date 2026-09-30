using System.Security.Cryptography;
using System.Text;

namespace PaymentGateway.Domain.Services;

public static class IdempotencyPayloadHasher
{
    public static string ComputeHash(
        string commandType,
        string? paymentId,
        string partyId,
        long amount,
        string currency)
    {
        var rawPayload = $"{commandType}:{paymentId ?? string.Empty}:{partyId}:{amount}:{currency}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawPayload));
        return Convert.ToHexStringLower(hashBytes);
    }
}
