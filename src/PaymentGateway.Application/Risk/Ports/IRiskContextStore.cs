using PaymentGateway.Domain.Entities;

namespace PaymentGateway.Application.Risk.Ports;

public interface IRiskContextStore
{
    Task RecordContextAsync(PartyRiskContext context, CancellationToken ct = default);
    Task<IReadOnlyList<PartyRiskContext>> GetPartyHistoryAsync(string partyId, CancellationToken ct = default);
    Task<IReadOnlyList<PartyRiskContext>> FindSimilarContextsAsync(string partyId, float[] targetEmbedding, int limit = 5, CancellationToken ct = default);
}
