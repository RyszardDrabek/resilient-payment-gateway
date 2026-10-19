namespace PaymentGateway.Domain.Ports;

public record SettlementResult(
    bool IsAuthorized,
    string Channel,
    string ChannelReference,
    string? DeclineReason = null,
    bool IsUnanswered = false,
    string? MerchantReference = null,
    bool IsConfigurationFail = false,
    PaymentGateway.Domain.Entities.ThreeDsActionDetails? ThreeDsAction = null)
{
    public bool IsSuccessful => IsAuthorized && !IsUnanswered && !IsConfigurationFail && !IsActionRequired;
    public bool IsActionRequired => ThreeDsAction != null;

    public static SettlementResult Success(string channel, string channelReference, string? merchantReference = null) =>
        new(true, channel, channelReference, null, false, merchantReference, false);

    public static SettlementResult ActionRequired(
        string channel,
        string channelReference,
        PaymentGateway.Domain.Entities.ThreeDsActionDetails actionDetails,
        string? merchantReference = null) =>
        new(false, channel, channelReference, null, false, merchantReference, false, actionDetails);

    public static SettlementResult Declined(string channel, string? channelReference, string declineReason, string? merchantReference = null) =>
        new(false, channel, channelReference ?? string.Empty, declineReason, false, merchantReference, false);

    public static SettlementResult Unanswered(
        string channel,
        string declineReason,
        string? merchantReference = null,
        string? channelReference = null) =>
        new(false, channel, channelReference ?? (!string.IsNullOrWhiteSpace(merchantReference) && merchantReference.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? merchantReference : string.Empty), declineReason, true, merchantReference, false);

    public static SettlementResult ConfigurationFail(string channel, string declineReason, string? merchantReference = null) =>
        new(false, channel, string.Empty, declineReason, false, merchantReference, true);
}
