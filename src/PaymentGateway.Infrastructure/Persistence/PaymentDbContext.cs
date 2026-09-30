using Microsoft.EntityFrameworkCore;

namespace PaymentGateway.Infrastructure.Persistence;

/// <summary>PAY/outbox DbContext. Tables arrive with features; scaffold keeps an empty model + migration.</summary>
public sealed class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    public DbSet<PaymentGateway.Domain.Entities.Payment> Payments => Set<PaymentGateway.Domain.Entities.Payment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("pay");

        modelBuilder.Entity<PaymentGateway.Domain.Entities.Payment>(b =>
        {
            b.ToTable("Payments");
            b.HasKey(p => p.Id);
            b.Property(p => p.PartyId).IsRequired().HasMaxLength(128);
            b.Property(p => p.Currency).IsRequired().HasMaxLength(3);
            b.Property(p => p.SettlementChannel).IsRequired().HasMaxLength(64);
            b.Property(p => p.ChannelReference).HasMaxLength(128);
            b.Property(p => p.DeclineReason).HasMaxLength(256);
            b.Property(p => p.State).HasConversion<string>();
        });
    }
}
