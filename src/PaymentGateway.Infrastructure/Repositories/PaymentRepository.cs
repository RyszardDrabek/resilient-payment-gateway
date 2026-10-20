using Microsoft.EntityFrameworkCore;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Ports;
using PaymentGateway.Infrastructure.Persistence;

namespace PaymentGateway.Infrastructure.Repositories;

public sealed class PaymentRepository(PaymentDbContext dbContext) : IPaymentRepository
{
    public async Task AddAsync(Payment payment, CancellationToken ct = default)
    {
        dbContext.Payments.Add(payment);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Payment payment, CancellationToken ct = default)
    {
        dbContext.Payments.Update(payment);
        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new PaymentGateway.Domain.Exceptions.PaymentConcurrencyException(payment.Id);
        }
    }

    public async Task<Payment?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        return await dbContext.Payments.FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<Payment?> GetByChannelReferenceAsync(string channelReference, CancellationToken ct = default)
    {
        return await dbContext.Payments.FirstOrDefaultAsync(p => p.ChannelReference == channelReference, ct);
    }

    public async Task<IReadOnlyList<Payment>> GetUnresolvedAsync(CancellationToken ct = default)
    {
        return await dbContext.Payments
            .Where(p => p.State == PaymentState.Pending || p.State == PaymentState.Unknown)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<(IReadOnlyList<Payment> Items, int TotalCount)> GetUnresolvedPaginatedAsync(
        DateTimeOffset createdBefore,
        string? channel = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default)
    {
        var query = dbContext.Payments
            .Where(p => (p.State == PaymentState.Pending || p.State == PaymentState.Unknown) &&
                        p.CreatedAt <= createdBefore);

        if (!string.IsNullOrWhiteSpace(channel))
        {
            var normalizedChannel = channel.Trim();
            query = query.Where(p => EF.Functions.ILike(p.SettlementChannel, normalizedChannel));
        }

        var totalCount = await query.CountAsync(ct);

        var safePage = page < 1 ? 1 : page;
        var safePageSize = pageSize < 1 ? 50 : (pageSize > 100 ? 100 : pageSize);

        var items = await query
            .OrderBy(p => p.CreatedAt)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<IReadOnlyList<Payment>> GetExpiredThreeDsPaymentsAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        return await dbContext.Payments
            .Where(p => p.State == PaymentState.RequiresAction &&
                        p.ThreeDsAction != null &&
                        p.ThreeDsAction.ExpiresAt != null &&
                        p.ThreeDsAction.ExpiresAt <= now)
            .OrderBy(p => p.CreatedAt)
            .Take(100)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Payment>> GetCapturedPaymentsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        string? partyId = null,
        string? currency = null,
        CancellationToken ct = default)
    {
        var query = dbContext.Payments
            .Where(p => p.CapturedAmount > 0 && p.CreatedAt >= from && p.CreatedAt <= to);

        if (!string.IsNullOrWhiteSpace(partyId))
        {
            query = query.Where(p => p.PartyId == partyId);
        }

        if (!string.IsNullOrWhiteSpace(currency))
        {
            var normalizedCurrency = currency.Trim().ToUpperInvariant();
            query = query.Where(p => p.Currency == normalizedCurrency);
        }

        return await query.OrderBy(p => p.CreatedAt).ToListAsync(ct);
    }
}
