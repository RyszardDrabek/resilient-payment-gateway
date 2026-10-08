namespace PaymentGateway.Domain.Ports;

public interface ISettlementPort
{
    Task<SettlementResult> AuthorizeAsync(
        string partyId,
        long amount,
        string currency,
        string? channel = null,
        CancellationToken ct = default);

    Task<SettlementResult> AuthorizeAsync(
        string partyId,
        long amount,
        string currency,
        string? channel,
        string? idempotencyKey,
        CancellationToken ct = default) =>
        AuthorizeAsync(partyId, amount, currency, channel, ct);

    Task<SettlementResult> CaptureAsync(
        string paymentId,
        string channelReference,
        long amount,
        string currency,
        string? channel = null,
        CancellationToken ct = default);

    Task<SettlementResult> RefundAsync(
        string paymentId,
        string channelReference,
        long amount,
        string currency,
        string? channel = null,
        CancellationToken ct = default);

    Task<SettlementResult> CancelAsync(
        string paymentId,
        string channelReference,
        string? channel = null,
        CancellationToken ct = default);

    Task<SettlementResult> QueryPaymentStatusAsync(
        string paymentId,
        string? channelReference = null,
        string? merchantReference = null,
        string? channel = null,
        CancellationToken ct = default) =>
        Task.FromResult(SettlementResult.Unanswered(channel ?? "UNKNOWN", "Status query not supported by settlement channel"));
}

