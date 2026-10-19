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
    DateTimeOffset CreatedAt,
    long CapturedAmount = 0,
    long RemainingAuthorizedAmount = 0,
    long RefundedAmount = 0,
    long RemainingRefundableAmount = 0,
    ThreeDsActionDto? Action = null);
