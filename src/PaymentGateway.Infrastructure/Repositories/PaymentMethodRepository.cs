using Microsoft.EntityFrameworkCore;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Ports;
using PaymentGateway.Infrastructure.Persistence;

namespace PaymentGateway.Infrastructure.Repositories;

public sealed class PaymentMethodRepository(PaymentDbContext dbContext) : IPaymentMethodRepository
{
    public async Task<PaymentMethodToken?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return await dbContext.PaymentMethods
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<PaymentMethodToken>> ListByPartyIdAsync(
        string partyId,
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.PaymentMethods.Where(m => m.PartyId == partyId);
        if (activeOnly)
        {
            query = query.Where(m => m.Status == PaymentMethodStatus.Active);
        }

        return await query
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task SaveAsync(PaymentMethodToken paymentMethod, CancellationToken cancellationToken = default)
    {
        var exists = await dbContext.PaymentMethods.AnyAsync(m => m.Id == paymentMethod.Id, cancellationToken);
        if (!exists)
        {
            dbContext.PaymentMethods.Add(paymentMethod);
        }
        else
        {
            dbContext.PaymentMethods.Update(paymentMethod);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
