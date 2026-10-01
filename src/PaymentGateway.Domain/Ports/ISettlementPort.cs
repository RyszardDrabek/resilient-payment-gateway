namespace PaymentGateway.Domain.Ports;

public interface ISettlementPort
{
    Task<SettlementResult> AuthorizeAsync(
        string partyId,
        long amount,
        string currency,
        string? channel = null,
        CancellationToken ct = default);

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
}
