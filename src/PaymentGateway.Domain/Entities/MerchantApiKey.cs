using System.Security.Cryptography;
using System.Text;

namespace PaymentGateway.Domain.Entities;

public sealed class MerchantApiKey
{
    public Guid Id { get; private set; }
    public string MerchantId { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Prefix { get; private set; } = string.Empty;
    public string KeyHash { get; private set; } = string.Empty;
    public string Salt { get; private set; } = string.Empty;
    public List<string> Scopes { get; private set; } = [];
    public DateTimeOffset CreatedAt { get; private set; }
    public bool IsRevoked { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? RevokedBy { get; private set; }

    private MerchantApiKey() { } // EF Core

    public static (MerchantApiKey Entity, string RawKey) Create(
        string merchantId,
        string name,
        IEnumerable<string> scopes,
        bool isLive = true)
    {
        if (string.IsNullOrWhiteSpace(merchantId))
        {
            throw new ArgumentException("Merchant ID cannot be empty.", nameof(merchantId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name cannot be empty.", nameof(name));
        }

        var randomBytes = new byte[32];
        RandomNumberGenerator.Fill(randomBytes);
        var secretHex = Convert.ToHexString(randomBytes).ToLowerInvariant();

        var prefixBase = isLive ? "rpg_live_" : "rpg_test_";
        var prefix = prefixBase + secretHex[..10];
        var rawKey = $"{prefix}_{secretHex}";

        var saltBytes = new byte[16];
        RandomNumberGenerator.Fill(saltBytes);
        var salt = Convert.ToBase64String(saltBytes);

        var keyHash = ComputeHash(rawKey, saltBytes);

        var entity = new MerchantApiKey
        {
            Id = Guid.NewGuid(),
            MerchantId = merchantId.Trim(),
            Name = name.Trim(),
            Prefix = prefix,
            KeyHash = keyHash,
            Salt = salt,
            Scopes = scopes.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList(),
            CreatedAt = DateTimeOffset.UtcNow,
            IsRevoked = false,
            RevokedAt = null,
            RevokedBy = null
        };

        return (entity, rawKey);
    }

    public bool VerifyKey(string rawKey)
    {
        if (IsRevoked || string.IsNullOrWhiteSpace(rawKey))
        {
            return false;
        }

        if (!rawKey.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var saltBytes = Convert.FromBase64String(Salt);
        var computedHash = ComputeHash(rawKey, saltBytes);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(KeyHash),
            Encoding.UTF8.GetBytes(computedHash));
    }

    public void Revoke(string? revokedBy = null)
    {
        if (!IsRevoked)
        {
            IsRevoked = true;
            RevokedAt = DateTimeOffset.UtcNow;
            RevokedBy = string.IsNullOrWhiteSpace(revokedBy) ? null : revokedBy.Trim();
        }
    }

    public bool HasScope(string scope)
    {
        return Scopes.Contains(scope, StringComparer.OrdinalIgnoreCase);
    }

    private static string ComputeHash(string rawKey, byte[] saltBytes)
    {
        var rawKeyBytes = Encoding.UTF8.GetBytes(rawKey);
        var combined = new byte[saltBytes.Length + rawKeyBytes.Length];
        Buffer.BlockCopy(saltBytes, 0, combined, 0, saltBytes.Length);
        Buffer.BlockCopy(rawKeyBytes, 0, combined, saltBytes.Length, rawKeyBytes.Length);

        var hashBytes = SHA256.HashData(combined);
        return Convert.ToBase64String(hashBytes);
    }
}
