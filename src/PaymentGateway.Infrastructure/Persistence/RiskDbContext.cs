using Microsoft.EntityFrameworkCore;
using PaymentGateway.Domain.Entities;

namespace PaymentGateway.Infrastructure.Persistence;

/// <summary>RISK DbContext with a dedicated connection pool (ADR-009).</summary>
public sealed class RiskDbContext(DbContextOptions<RiskDbContext> options) : DbContext(options)
{
    public DbSet<RiskVerdict> RiskVerdicts => Set<RiskVerdict>();
    public DbSet<RiskFlag> RiskFlags => Set<RiskFlag>();
    public DbSet<PartyRiskContext> PartyRiskContexts => Set<PartyRiskContext>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("risk");

        modelBuilder.Entity<RiskVerdict>(b =>
        {
            b.ToTable("RiskVerdicts");
            b.HasKey(v => v.Id);
            b.Property(v => v.EventId).IsRequired().HasMaxLength(128);
            b.Property(v => v.PaymentId).IsRequired().HasMaxLength(128);
            b.Property(v => v.PartyId).IsRequired().HasMaxLength(128);
            b.Property(v => v.Status).HasConversion<string>().HasMaxLength(32);
            b.Property(v => v.Reason).HasMaxLength(512);
            b.Property(v => v.FlagId).HasMaxLength(128);
            b.Property(v => v.ScoredAt).IsRequired();

            b.HasIndex(v => v.EventId).IsUnique();
            b.HasIndex(v => v.PaymentId);
        });

        modelBuilder.Entity<RiskFlag>(b =>
        {
            b.ToTable("RiskFlags");
            b.HasKey(f => f.Id);
            b.Property(f => f.PaymentId).IsRequired().HasMaxLength(128);
            b.Property(f => f.PartyId).IsRequired().HasMaxLength(128);
            b.Property(f => f.EventId).IsRequired().HasMaxLength(128);
            b.Property(f => f.Reason).IsRequired().HasMaxLength(512);
            b.Property(f => f.Status).IsRequired().HasMaxLength(32);
            b.Property(f => f.Disposition).HasMaxLength(32);
            b.Property(f => f.DispositionedAt);
            b.Property(f => f.RaisedAt).IsRequired();

            b.HasIndex(f => f.PaymentId);
            b.HasIndex(f => f.PartyId);
            b.HasIndex(f => f.Status);
        });

        modelBuilder.Entity<PartyRiskContext>(b =>
        {
            b.ToTable("PartyRiskContexts");
            b.HasKey(c => c.Id);
            b.Property(c => c.PartyId).IsRequired().HasMaxLength(128);
            b.Property(c => c.PaymentId).IsRequired().HasMaxLength(128);
            b.Property(c => c.EventId).IsRequired().HasMaxLength(128);
            b.Property(c => c.Currency).IsRequired().HasMaxLength(12);
            b.Property(c => c.Amount).IsRequired();
            b.Property(c => c.RecordedAt).IsRequired();
            b.Property(c => c.Embedding);

            b.HasIndex(c => c.PartyId);
        });
    }
}
