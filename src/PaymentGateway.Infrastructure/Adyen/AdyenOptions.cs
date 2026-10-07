namespace PaymentGateway.Infrastructure.Adyen;

public sealed class AdyenOptions
{
    public const string SectionName = "Adyen";

    public string Mode { get; set; } = "WireMock";
    public string ApiVersion { get; set; } = AdyenEndpoints.DefaultApiVersion;
    public string? BaseUrl { get; set; }
    public string MerchantAccount { get; set; } = "InterparkingMockAccount";
    public string? ApiKey { get; set; }
    public int TimeoutSeconds { get; set; } = 5;
    public bool AutoStartMockServer { get; set; } = true;
    public int? MockServerPort { get; set; }

    public bool IsLiveMode =>
        string.Equals(Mode, "Live", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Mode, "Sandbox", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Mode, "LiveSandbox", StringComparison.OrdinalIgnoreCase);

    public bool HasValidCredentials =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(MerchantAccount) &&
        !string.Equals(ApiKey, "INVALID", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(ApiKey, "REPLACE_ME", StringComparison.OrdinalIgnoreCase);
}
