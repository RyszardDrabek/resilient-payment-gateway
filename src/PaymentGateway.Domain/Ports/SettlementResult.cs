namespace PaymentGateway.Domain.Ports;

public record SettlementResult(
    bool IsAuthorized,
    string Channel,
    string ChannelReference,
    string? DeclineReason = null,
    bool IsUnanswered = false,
    string? MerchantReference = null,
    bool IsConfigurationFail = false)
{
    public bool IsSuccessful => IsAuthorized && !IsUnanswered && !IsConfigurationFail;

    public static SettlementResult Success(string channel, string channelReference, string? merchantReference = null) =>
        new(true, channel, channelReference, null, false, merchantReference, false);

    public static SettlementResult Declined(string channel, string? channelReference, string declineReason, string? merchantReference = null) =>
        new(false, channel, channelReference ?? string.Empty, declineReason, false, merchantReference, false);

    public static SettlementResult Unanswered(string channel, string declineReason, string? merchantReference = null) =>
        new(false, channel, string.Empty, declineReason, true, merchantReference, false);

    public static SettlementResult ConfigurationFail(string channel, string declineReason, string? merchantReference = null) =>
        new(false, channel, string.Empty, declineReason, false, merchantReference, true);
}
