namespace PaymentGateway.Application.ApiKeys;

public sealed record MerchantApiKeyCreatedDto(
    Guid Id,
    string MerchantId,
    string Name,
    string Prefix,
    IReadOnlyList<string> Scopes,
    DateTimeOffset CreatedAt,
    string RawKey);

public sealed record MerchantApiKeySummaryDto(
    Guid Id,
    string MerchantId,
    string Name,
    string Prefix,
    IReadOnlyList<string> Scopes,
    DateTimeOffset CreatedAt,
    bool IsRevoked,
    DateTimeOffset? RevokedAt,
    string? RevokedBy = null);

public sealed record ValidatedApiKeyDto(
    bool IsValid,
    string? MerchantId,
    IReadOnlyList<string>? Scopes);
