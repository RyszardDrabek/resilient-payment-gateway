using Microsoft.EntityFrameworkCore;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Ports;
using PaymentGateway.Infrastructure.Persistence;

namespace PaymentGateway.Infrastructure.Repositories;

public sealed class FeeScheduleRepository(PaymentDbContext dbContext) : IFeeScheduleRepository
{
    public async Task<FeeSchedule?> GetActiveFeeScheduleAsync(
        string merchantId,
        string currency,
        DateTimeOffset asOf,
        CancellationToken ct = default)
    {
        var normalizedCurrency = currency.Trim().ToUpperInvariant();

        return await dbContext.FeeSchedules
            .Where(f => f.MerchantId == merchantId
                     && f.Currency == normalizedCurrency
                     && f.EffectiveFrom <= asOf
                     && (f.EffectiveTo == null || f.EffectiveTo >= asOf))
            .OrderByDescending(f => f.EffectiveFrom)
            .FirstOrDefaultAsync(ct);
    }

    public async Task SaveAsync(FeeSchedule schedule, CancellationToken ct = default)
    {
        var exists = await dbContext.FeeSchedules.AnyAsync(f => f.Id == schedule.Id, ct);
        if (!exists)
        {
            dbContext.FeeSchedules.Add(schedule);
        }
        else
        {
            dbContext.FeeSchedules.Update(schedule);
        }

        await dbContext.SaveChangesAsync(ct);
    }
}
