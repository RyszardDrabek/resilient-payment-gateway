namespace PaymentGateway.Application.Web3;

public enum Web3SettlementStatus
{
    Pending,
    Settled,
    ReorgPending
}

public static class Web3SettlementStatusExtensions
{
    public const string Pending = "pending";
    public const string Settled = "settled";
    public const string ReorgPending = "reorg-pending";

    public static string ToStatusString(this Web3SettlementStatus status) => status switch
    {
        Web3SettlementStatus.Pending => Pending,
        Web3SettlementStatus.Settled => Settled,
        Web3SettlementStatus.ReorgPending => ReorgPending,
        _ => status.ToString().ToLowerInvariant()
    };
}

public record Web3SettlementObservation(
    string PaymentId,
    string TransactionHash,
    int ConfirmationDepth,
    int RequiredConfirmations,
    Web3SettlementStatus Status,
    string? Message = null);
