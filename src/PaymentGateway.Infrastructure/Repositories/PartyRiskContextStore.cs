using Microsoft.EntityFrameworkCore;
using PaymentGateway.Application.Risk.Ports;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Infrastructure.Persistence;

namespace PaymentGateway.Infrastructure.Repositories;

public sealed class PartyRiskContextStore(RiskDbContext db) : IRiskContextStore
{
    public async Task RecordContextAsync(PartyRiskContext context, CancellationToken ct = default)
    {
        await db.PartyRiskContexts.AddAsync(context, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PartyRiskContext>> GetPartyHistoryAsync(string partyId, CancellationToken ct = default) =>
        await db.PartyRiskContexts.AsNoTracking()
            .Where(c => c.PartyId == partyId)
            .OrderByDescending(c => c.RecordedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<PartyRiskContext>> FindSimilarContextsAsync(
        string partyId,
        float[] targetEmbedding,
        int limit = 5,
        CancellationToken ct = default)
    {
        var records = await db.PartyRiskContexts.AsNoTracking()
            .Where(c => c.PartyId == partyId)
            .ToListAsync(ct);

        if (records.Count == 0 || targetEmbedding.Length == 0)
        {
            return [];
        }

        return records
            .Select(r => new { Record = r, Similarity = ComputeCosineSimilarity(r.Embedding, targetEmbedding) })
            .OrderByDescending(x => x.Similarity)
            .Take(limit)
            .Select(x => x.Record)
            .ToList();
    }

    public static float ComputeCosineSimilarity(float[] a, float[] b)
    {
        if (a is null || b is null || a.Length == 0 || b.Length == 0 || a.Length != b.Length)
        {
            return 0f;
        }

        float dot = 0f;
        float normA = 0f;
        float normB = 0f;

        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA <= 0f || normB <= 0f)
        {
            return 0f;
        }

        return dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
    }
}
