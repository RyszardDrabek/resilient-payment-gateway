namespace PaymentGateway.Application.Payments;

public record PaymentDto(
    string PaymentId,
    string PartyId,
    long Amount,
    string Currency,
    string SettlementChannel,
    string State,
    string? ChannelReference,
    string? DeclineReason,
    DateTimeOffset CreatedAt);
