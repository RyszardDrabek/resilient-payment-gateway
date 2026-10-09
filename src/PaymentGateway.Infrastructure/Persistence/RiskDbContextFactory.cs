using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PaymentGateway.Infrastructure.Persistence;

public sealed class RiskDbContextFactory : IDesignTimeDbContextFactory<RiskDbContext>
{
    public RiskDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<RiskDbContext>()
            .UseNpgsql(
                "Host=localhost;Port=5432;Database=payment_gateway;Username=postgres;Password=postgres",
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "risk"))
            .Options;
        return new RiskDbContext(options);
    }
}
