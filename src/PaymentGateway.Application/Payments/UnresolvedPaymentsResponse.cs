namespace PaymentGateway.Application.Payments;

public sealed record UnresolvedPaymentsResponse(
    IReadOnlyList<UnresolvedPaymentSummaryDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int AgeMinutes,
    string? Channel);
