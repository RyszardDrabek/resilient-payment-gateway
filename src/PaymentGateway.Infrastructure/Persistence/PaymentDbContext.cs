using Microsoft.EntityFrameworkCore;

namespace PaymentGateway.Infrastructure.Persistence;

/// <summary>PAY/outbox DbContext. Tables arrive with features; scaffold keeps an empty model + migration.</summary>
public sealed class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    public DbSet<PaymentGateway.Domain.Entities.Payment> Payments => Set<PaymentGateway.Domain.Entities.Payment>();
    public DbSet<PaymentGateway.Domain.Entities.IdempotencyRecord> IdempotencyRecords => Set<PaymentGateway.Domain.Entities.IdempotencyRecord>();

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

        modelBuilder.Entity<PaymentGateway.Domain.Entities.IdempotencyRecord>(b =>
        {
            b.ToTable("IdempotencyRecords");
            b.HasKey(r => r.Id);
            b.Property(r => r.Key).IsRequired().HasMaxLength(128);
            b.Property(r => r.CommandType).IsRequired().HasMaxLength(64);
            b.Property(r => r.PaymentId).HasMaxLength(128);
            b.Property(r => r.PayloadHash).IsRequired().HasMaxLength(128);
            b.Property(r => r.Status).HasConversion<string>();
            b.Property(r => r.ResponseStatusCode);
            b.Property(r => r.ResponsePayload);
            b.Property(r => r.CreatedAt).IsRequired();
            b.Property(r => r.ExpiresAt).IsRequired();
            b.Property(r => r.LockedUntil);

            b.HasIndex(r => new { r.CommandType, r.Key })
                .HasFilter("\"CommandType\" = 'Authorize'")
                .IsUnique();

            b.HasIndex(r => new { r.PaymentId, r.CommandType, r.Key })
                .HasFilter("\"CommandType\" != 'Authorize'")
                .IsUnique();
        });
    }
}
