namespace PaymentGateway.Infrastructure.Adyen;

public static class AdyenEndpoints
{
    public const string DefaultApiVersion = "v71";

    public static string Payments(string version = DefaultApiVersion) =>
        $"/{version}/payments";

    public static string Captures(string channelReference, string version = DefaultApiVersion) =>
        $"/{version}/payments/{channelReference}/captures";

    public static string Refunds(string channelReference, string version = DefaultApiVersion) =>
        $"/{version}/payments/{channelReference}/refunds";

    public static string Cancels(string channelReference, string version = DefaultApiVersion) =>
        $"/{version}/payments/{channelReference}/cancels";

    public static string PaymentsPathPrefix(string version = DefaultApiVersion) =>
        $"/{version}/payments/";
}
