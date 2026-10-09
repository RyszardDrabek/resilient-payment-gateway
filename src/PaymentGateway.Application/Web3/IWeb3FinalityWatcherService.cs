namespace PaymentGateway.Application.Web3;

public interface IWeb3FinalityWatcherService
{
    Task<Web3SettlementObservation> ObserveAndApplyFinalityAsync(string paymentId, CancellationToken ct = default);

    Task<IReadOnlyList<Web3SettlementObservation>> WatchAllPendingSettlementsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<Web3SettlementObservation>> GetPendingSettlementsAsync(CancellationToken ct = default);
}
