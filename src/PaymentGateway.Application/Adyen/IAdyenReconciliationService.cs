using PaymentGateway.Domain.Entities;

namespace PaymentGateway.Application.Adyen;

public enum ReconciliationStatus
{
    Resolved,
    Unresolved,
    Uncorrelated,
    AlreadyProcessed,
    NotFound
}

public sealed record ReconciliationResult(
    ReconciliationStatus Status,
    string? PaymentId = null,
    string? Outcome = null,
    string? Message = null);

public interface IAdyenReconciliationService
{
    Task<ReconciliationResult> ReconcileNotificationAsync(Guid notificationId, CancellationToken ct = default);
    Task<ReconciliationResult> ReconcileUnresolvedPaymentAsync(string paymentId, TimeSpan? reconciliationWindow = null, CancellationToken ct = default);
    Task<IReadOnlyList<AdyenNotification>> GetUncorrelatedNotificationsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Payment>> GetUnresolvedPaymentsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ReconciliationResult>> ReconcileAllPendingAsync(CancellationToken ct = default);
}
