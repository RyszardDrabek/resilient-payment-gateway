using PaymentGateway.Domain.Entities;

namespace PaymentGateway.Domain.Ports;

public record SettlementResult(bool IsAuthorized, string ChannelReference, string? DeclineReason = null);

public interface ISettlementPort
{
    Task<SettlementResult> AuthorizeAsync(
        string partyId,
        long amount,
        string currency,
        string channel,
        CancellationToken ct = default);
}

public interface IPaymentRepository
{
    Task AddAsync(Payment payment, CancellationToken ct = default);
    Task<Payment?> GetByIdAsync(string id, CancellationToken ct = default);
}
