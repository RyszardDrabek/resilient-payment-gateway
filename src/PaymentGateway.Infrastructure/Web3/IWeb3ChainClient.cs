namespace PaymentGateway.Infrastructure.Web3;

public record Web3TransactionReceipt(
    string TransactionHash,
    string From,
    string To,
    long Amount,
    string Asset,
    bool IsSuccess,
    int Confirmations,
    DateTimeOffset BlockTimestamp);

public interface IWeb3ChainClient
{
    Task<string> BroadcastTransferAsync(
        string fromAddress,
        string toAddress,
        long amount,
        string asset,
        string? idempotencyToken = null,
        CancellationToken ct = default);

    Task<Web3TransactionReceipt?> GetReceiptAsync(
        string transactionHash,
        CancellationToken ct = default);

    Task<bool> IsFinalizedAsync(
        string transactionHash,
        int requiredConfirmations,
        CancellationToken ct = default);
}
