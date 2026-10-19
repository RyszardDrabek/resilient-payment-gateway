using PaymentGateway.Domain.Entities;

namespace PaymentGateway.Application.Payouts;

public record PayoutItemDto(
    string Id,
    string PaymentId,
    long GrossAmount,
    long FeeAmount,
    long NetAmount,
    string Currency,
    DateTimeOffset CapturedAt);

public record MerchantPayoutBatchDto(
    string Id,
    string MerchantId,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    long GrossAmount,
    long FeeAmount,
    long NetAmount,
    string Currency,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExecutedAt,
    string? FailureReason,
    IReadOnlyList<PayoutItemDto> Items);

public static class PayoutMapper
{
    public static PayoutItemDto ToDto(this PayoutItem item) =>
        new(
            item.Id,
            item.PaymentId,
            item.GrossAmount,
            item.FeeAmount,
            item.NetAmount,
            item.Currency,
            item.CapturedAt);

    public static MerchantPayoutBatchDto ToDto(this MerchantPayoutBatch batch) =>
        new(
            batch.Id,
            batch.MerchantId,
            batch.PeriodStart,
            batch.PeriodEnd,
            batch.GrossAmount,
            batch.FeeAmount,
            batch.NetAmount,
            batch.Currency,
            batch.Status.ToString(),
            batch.CreatedAt,
            batch.ExecutedAt,
            batch.FailureReason,
            batch.Items.Select(i => i.ToDto()).ToList());
}
