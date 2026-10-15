using PaymentGateway.Application.ApiKeys;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.UnitTests.Application;

public sealed class MerchantApiKeyHandlersTests
{
    private sealed class InMemoryMerchantApiKeyRepository : IMerchantApiKeyRepository
    {
        public readonly Dictionary<Guid, MerchantApiKey> Keys = [];

        public Task AddAsync(MerchantApiKey apiKey, CancellationToken ct = default)
        {
            Keys[apiKey.Id] = apiKey;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<MerchantApiKey>> ListByMerchantAsync(string merchantId, CancellationToken ct = default)
        {
            var list = Keys.Values
                .Where(k => string.Equals(k.MerchantId, merchantId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return Task.FromResult<IReadOnlyList<MerchantApiKey>>(list);
        }

        public Task<MerchantApiKey?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            Keys.TryGetValue(id, out var key);
            return Task.FromResult(key);
        }

        public Task<MerchantApiKey?> FindByPrefixAsync(string prefix, CancellationToken ct = default)
        {
            var key = Keys.Values.FirstOrDefault(k => string.Equals(k.Prefix, prefix, StringComparison.Ordinal));
            return Task.FromResult(key);
        }

        public Task UpdateAsync(MerchantApiKey apiKey, CancellationToken ct = default)
        {
            Keys[apiKey.Id] = apiKey;
            return Task.CompletedTask;
        }
    }

    private readonly InMemoryMerchantApiKeyRepository _repository = new();

    [Fact]
    public async Task CreateMerchantApiKeyCommandHandler_PersistsAndReturnsKeyWithSecret()
    {
        // Arrange
        var handler = new CreateMerchantApiKeyCommandHandler(_repository);
        var command = new CreateMerchantApiKeyCommand(
            MerchantId: "merch_acme",
            Name: "Integration Backend",
            Scopes: ["payments:write", "payments:read"],
            IsLive: true);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("merch_acme", result.MerchantId);
        Assert.Equal("Integration Backend", result.Name);
        Assert.StartsWith("rpg_live_", result.Prefix);
        Assert.StartsWith(result.Prefix, result.RawKey);
        Assert.Equal(2, result.Scopes.Count);

        Assert.True(_repository.Keys.ContainsKey(result.Id));
        var persisted = _repository.Keys[result.Id];
        Assert.Equal("merch_acme", persisted.MerchantId);
        Assert.Equal(result.Prefix, persisted.Prefix);
    }

    [Fact]
    public async Task ListMerchantApiKeysQueryHandler_ReturnsSummariesForMerchant()
    {
        // Arrange
        var (key1, _) = MerchantApiKey.Create("merch_acme", "Key 1", ["payments:read"], true);
        var (key2, _) = MerchantApiKey.Create("merch_acme", "Key 2", ["payments:write"], true);
        key2.Revoke();

        await _repository.AddAsync(key1);
        await _repository.AddAsync(key2);

        var handler = new ListMerchantApiKeysQueryHandler(_repository);
        var query = new ListMerchantApiKeysQuery("merch_acme");

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.False(result[0].IsRevoked);
        Assert.Null(result[0].RevokedBy);
        Assert.True(result[1].IsRevoked);
        Assert.NotNull(result[1].RevokedAt);
    }

    [Fact]
    public async Task RevokeMerchantApiKeyCommandHandler_RevokesExistingKeyWithAuditedCaller()
    {
        // Arrange
        var (key, _) = MerchantApiKey.Create("merch_acme", "Key 1", ["payments:read"], true);
        await _repository.AddAsync(key);

        var handler = new RevokeMerchantApiKeyCommandHandler(_repository);
        var command = new RevokeMerchantApiKeyCommand("merch_acme", key.Id, "admin_user_42");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result);
        Assert.True(key.IsRevoked);
        Assert.Equal("admin_user_42", key.RevokedBy);
    }

    [Fact]
    public async Task RevokeMerchantApiKeyCommandHandler_WhenKeyBelongsToDifferentMerchant_ReturnsFalse()
    {
        // Arrange
        var (key, _) = MerchantApiKey.Create("merch_other", "Key 1", ["payments:read"], true);
        await _repository.AddAsync(key);

        var handler = new RevokeMerchantApiKeyCommandHandler(_repository);
        var command = new RevokeMerchantApiKeyCommand("merch_acme", key.Id);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result);
        Assert.False(key.IsRevoked);
    }

    [Fact]
    public async Task ValidateMerchantApiKeyQueryHandler_ValidKey_ReturnsValidWithClaims()
    {
        // Arrange
        var (key, rawKey) = MerchantApiKey.Create("merch_acme", "Key 1", ["payments:write", "payments:read"], true);
        await _repository.AddAsync(key);

        var handler = new ValidateMerchantApiKeyQueryHandler(_repository);
        var query = new ValidateMerchantApiKeyQuery(rawKey);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal("merch_acme", result.MerchantId);
        Assert.Contains("payments:write", result.Scopes!);
    }

    [Fact]
    public async Task ValidateMerchantApiKeyQueryHandler_InvalidOrRevokedKey_ReturnsInvalid()
    {
        // Arrange
        var (key, rawKey) = MerchantApiKey.Create("merch_acme", "Key 1", ["payments:write"], true);
        key.Revoke();
        await _repository.AddAsync(key);

        var handler = new ValidateMerchantApiKeyQueryHandler(_repository);
        var query = new ValidateMerchantApiKeyQuery(rawKey);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.False(result.IsValid);
        Assert.Null(result.MerchantId);
    }
}
