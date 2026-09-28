using Microsoft.EntityFrameworkCore;

namespace PaymentGateway.Infrastructure.Persistence;

/// <summary>PAY/outbox DbContext. Tables arrive with features; scaffold keeps an empty model + migration.</summary>
public sealed class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("pay");
    }
}
