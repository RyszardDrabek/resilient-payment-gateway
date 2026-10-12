namespace PaymentGateway.Application.Payments;

public sealed record UnresolvedPaymentSummaryDto(
    string PaymentId,
    string State,
    string SettlementChannel,
    string? ChannelReference,
    DateTimeOffset CreatedAt,
    int DurationElapsedMinutes);
