using Microsoft.EntityFrameworkCore;

namespace PaymentGateway.Infrastructure.Persistence;

/// <summary>RISK DbContext with a dedicated connection pool (ADR-009). Empty until F-RISK-*.</summary>
public sealed class RiskDbContext(DbContextOptions<RiskDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("risk");
    }
}
