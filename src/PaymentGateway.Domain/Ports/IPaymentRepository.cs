using PaymentGateway.Domain.Entities;

namespace PaymentGateway.Domain.Ports;

public interface IPaymentRepository
{
    Task AddAsync(Payment payment, CancellationToken ct = default);
    Task UpdateAsync(Payment payment, CancellationToken ct = default);
    Task<Payment?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<Payment?> GetByChannelReferenceAsync(string channelReference, CancellationToken ct = default);
    Task<IReadOnlyList<Payment>> GetUnresolvedAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Payment>>([]);
    Task<(IReadOnlyList<Payment> Items, int TotalCount)> GetUnresolvedPaginatedAsync(
        DateTimeOffset createdBefore,
        string? channel = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default) =>
        Task.FromResult<(IReadOnlyList<Payment> Items, int TotalCount)>(([], 0));
    Task<IReadOnlyList<Payment>> GetExpiredThreeDsPaymentsAsync(DateTimeOffset now, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Payment>>([]);
    Task<IReadOnlyList<Payment>> GetCapturedPaymentsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        string? partyId = null,
        string? currency = null,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Payment>>([]);
}


