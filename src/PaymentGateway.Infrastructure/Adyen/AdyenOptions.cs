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
}
