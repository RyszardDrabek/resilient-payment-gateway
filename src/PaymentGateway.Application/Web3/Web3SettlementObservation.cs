namespace PaymentGateway.Application.Web3;

public enum Web3SettlementStatus
{
    Pending,
    Settled,
    ReorgPending
}

public record Web3SettlementObservation(
    string PaymentId,
    string TransactionHash,
    int ConfirmationDepth,
    int RequiredConfirmations,
    Web3SettlementStatus Status,
    string? Message = null);
