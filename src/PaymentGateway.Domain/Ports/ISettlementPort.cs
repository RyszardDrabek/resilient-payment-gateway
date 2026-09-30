namespace PaymentGateway.Domain.Ports;

public interface ISettlementPort
{
    Task<SettlementResult> AuthorizeAsync(
        string partyId,
        long amount,
        string currency,
        string? channel = null,
        CancellationToken ct = default);
}
