using Microsoft.EntityFrameworkCore;

namespace PaymentGateway.Infrastructure.Persistence;

/// <summary>PAY/outbox DbContext. Tables arrive with features; scaffold keeps an empty model + migration.</summary>
public sealed class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    public DbSet<PaymentGateway.Domain.Entities.Payment> Payments => Set<PaymentGateway.Domain.Entities.Payment>();
    public DbSet<PaymentGateway.Domain.Entities.IdempotencyRecord> IdempotencyRecords => Set<PaymentGateway.Domain.Entities.IdempotencyRecord>();
    public DbSet<PaymentGateway.Domain.Entities.AdyenNotification> AdyenNotifications => Set<PaymentGateway.Domain.Entities.AdyenNotification>();
    public DbSet<OutboxMessageRecord> OutboxMessages => Set<OutboxMessageRecord>();

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
            b.Property(p => p.Version).IsRequired().HasDefaultValue(1).IsConcurrencyToken();
            b.Ignore(p => p.DomainEvents);
        });

        modelBuilder.Entity<OutboxMessageRecord>(b =>
        {
            b.ToTable("OutboxMessages", "pay");
            b.HasKey(o => o.Id);
            b.Property(o => o.EventType).IsRequired().HasMaxLength(256);
            b.Property(o => o.Payload).IsRequired();
            b.Property(o => o.CreatedAt).IsRequired();
            b.Property(o => o.ProcessedAt);
            b.Property(o => o.DeliveryAttempts).IsRequired().HasDefaultValue(0);
            b.Property(o => o.Error).HasMaxLength(1024);
            b.HasIndex(o => o.ProcessedAt);
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

        modelBuilder.Entity<PaymentGateway.Domain.Entities.AdyenNotification>(b =>
        {
            b.ToTable("AdyenNotifications");
            b.HasKey(n => n.Id);
            b.Property(n => n.PspReference).IsRequired().HasMaxLength(128);
            b.Property(n => n.OriginalReference).HasMaxLength(128);
            b.Property(n => n.MerchantAccountCode).IsRequired().HasMaxLength(128);
            b.Property(n => n.MerchantReference).IsRequired().HasMaxLength(128);
            b.Property(n => n.EventCode).IsRequired().HasMaxLength(64);
            b.Property(n => n.AmountCurrency).IsRequired().HasMaxLength(3);
            b.Property(n => n.Reason).HasMaxLength(512);
            b.Property(n => n.CorrelatedPaymentId).HasMaxLength(128);
            b.Property(n => n.Status).IsRequired().HasMaxLength(64);
            b.Property(n => n.CreatedAt).IsRequired();

            b.HasIndex(n => new { n.PspReference, n.EventCode }).IsUnique();
            b.HasIndex(n => n.MerchantReference);
            b.HasIndex(n => n.CorrelatedPaymentId);
        });
    }
}
