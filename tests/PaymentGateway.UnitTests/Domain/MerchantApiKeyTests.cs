using PaymentGateway.Domain.Entities;

namespace PaymentGateway.UnitTests.Domain;

public sealed class MerchantApiKeyTests
{
    [Fact]
    public void Create_WithValidParameters_ReturnsEntityAndRawKey()
    {
        // Act
        var (entity, rawKey) = MerchantApiKey.Create(
            merchantId: "merch_acme",
            name: "Backend Server",
            scopes: ["payments:write", "payments:read"],
            isLive: true);

        // Assert
        Assert.NotEqual(Guid.Empty, entity.Id);
        Assert.Equal("merch_acme", entity.MerchantId);
        Assert.Equal("Backend Server", entity.Name);
        Assert.StartsWith("rpg_live_", entity.Prefix);
        Assert.StartsWith(entity.Prefix, rawKey);
        Assert.NotEmpty(entity.KeyHash);
        Assert.NotEmpty(entity.Salt);
        Assert.Equal(2, entity.Scopes.Count);
        Assert.Contains("payments:write", entity.Scopes);
        Assert.Contains("payments:read", entity.Scopes);
        Assert.False(entity.IsRevoked);
        Assert.Null(entity.RevokedAt);
        Assert.True(entity.CreatedAt <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Create_InTestMode_UsesTestPrefix()
    {
        // Act
        var (entity, rawKey) = MerchantApiKey.Create(
            merchantId: "merch_acme",
            name: "Test Client",
            scopes: ["payments:read"],
            isLive: false);

        // Assert
        Assert.StartsWith("rpg_test_", entity.Prefix);
        Assert.StartsWith(entity.Prefix, rawKey);
    }

    [Fact]
    public void VerifyKey_WithCorrectKey_ReturnsTrue()
    {
        // Arrange
        var (entity, rawKey) = MerchantApiKey.Create(
            merchantId: "merch_acme",
            name: "Backend Server",
            scopes: ["payments:write"],
            isLive: true);

        // Act & Assert
        Assert.True(entity.VerifyKey(rawKey));
    }

    [Fact]
    public void VerifyKey_WithTamperedKey_ReturnsFalse()
    {
        // Arrange
        var (entity, rawKey) = MerchantApiKey.Create(
            merchantId: "merch_acme",
            name: "Backend Server",
            scopes: ["payments:write"],
            isLive: true);

        var tamperedKey = rawKey + "wrong";

        // Act & Assert
        Assert.False(entity.VerifyKey(tamperedKey));
    }

    [Fact]
    public void VerifyKey_WhenRevoked_ReturnsFalse()
    {
        // Arrange
        var (entity, rawKey) = MerchantApiKey.Create(
            merchantId: "merch_acme",
            name: "Backend Server",
            scopes: ["payments:write"],
            isLive: true);

        // Act
        entity.Revoke();

        // Assert
        Assert.True(entity.IsRevoked);
        Assert.NotNull(entity.RevokedAt);
        Assert.Null(entity.RevokedBy);
        Assert.False(entity.VerifyKey(rawKey));
    }

    [Fact]
    public void Revoke_WithRevokedBy_RecordsAuditedCaller()
    {
        // Arrange
        var (entity, _) = MerchantApiKey.Create(
            merchantId: "merch_acme",
            name: "Backend Server",
            scopes: ["payments:write"],
            isLive: true);

        // Act
        entity.Revoke("operator_bob@example.com");

        // Assert
        Assert.True(entity.IsRevoked);
        Assert.NotNull(entity.RevokedAt);
        Assert.Equal("operator_bob@example.com", entity.RevokedBy);
    }

    [Fact]
    public void HasScope_ChecksRequestedScope()
    {
        // Arrange
        var (entity, _) = MerchantApiKey.Create(
            merchantId: "merch_acme",
            name: "Backend Server",
            scopes: ["payments:read"],
            isLive: true);

        // Assert
        Assert.True(entity.HasScope("payments:read"));
        Assert.False(entity.HasScope("payments:write"));
    }

    [Fact]
    public void Create_WithoutMerchantId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            MerchantApiKey.Create("", "Test", ["payments:read"], true));
    }

    [Fact]
    public void Create_WithoutName_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            MerchantApiKey.Create("merch_acme", " ", ["payments:read"], true));
    }
}
