namespace PaymentGateway.Domain.Ports;

public record SettlementResult(bool IsAuthorized, string ChannelReference, string? DeclineReason = null);
