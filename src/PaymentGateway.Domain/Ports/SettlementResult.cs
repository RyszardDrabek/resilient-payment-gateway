namespace PaymentGateway.Domain.Ports;

public record SettlementResult(
    bool IsAuthorized,
    string Channel,
    string ChannelReference,
    string? DeclineReason = null);
