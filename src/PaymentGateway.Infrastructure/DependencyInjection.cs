using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PaymentGateway.Infrastructure.Persistence;

namespace PaymentGateway.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Resolve connection strings from IConfiguration at DbContext creation time so tests/compose
        // can override ConnectionStrings without re-registering EF.
        _ = configuration.GetConnectionString("PaymentDb")
            ?? throw new InvalidOperationException("Connection string 'PaymentDb' is required.");

        services.AddDbContext<PaymentDbContext>((sp, options) =>
        {
            var cs = sp.GetRequiredService<IConfiguration>().GetConnectionString("PaymentDb")
                ?? throw new InvalidOperationException("Connection string 'PaymentDb' is required.");
            options.UseNpgsql(cs, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "pay"));
        });

        services.AddDbContext<RiskDbContext>((sp, options) =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var cs = config.GetConnectionString("RiskDb")
                ?? config.GetConnectionString("PaymentDb")
                ?? throw new InvalidOperationException("Connection string 'RiskDb' or 'PaymentDb' is required.");
            options.UseNpgsql(cs, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "risk"));
        });


        var useInMemory = configuration.GetValue("RabbitMq:UseInMemory", true);
        var rabbitHost = configuration["RabbitMq:Host"] ?? "localhost";
        var rabbitUser = configuration["RabbitMq:Username"] ?? "guest";
        var rabbitPass = configuration["RabbitMq:Password"] ?? "guest";

        services.AddMassTransit(x =>
        {
            if (useInMemory)
            {
                x.UsingInMemory((context, cfg) => cfg.ConfigureEndpoints(context));
            }
            else
            {
                x.UsingRabbitMq((context, cfg) =>
                {
                    cfg.Host(rabbitHost, "/", h =>
                    {
                        h.Username(rabbitUser);
                        h.Password(rabbitPass);
                    });
                    cfg.ConfigureEndpoints(context);
                });
            }
        });

        return services;
    }
}
