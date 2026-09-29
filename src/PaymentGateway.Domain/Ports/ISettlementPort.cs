namespace PaymentGateway.Domain.Ports;

public interface ISettlementPort
{
    Task<SettlementResult> AuthorizeAsync(
        string partyId,
        long amount,
        string currency,
        string channel,
        CancellationToken ct = default);
}
