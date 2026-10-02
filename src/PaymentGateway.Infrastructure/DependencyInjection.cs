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
        services.AddScoped<PaymentGateway.Domain.Ports.IPaymentRepository, PaymentGateway.Infrastructure.Repositories.PaymentRepository>();
        services.AddScoped<PaymentGateway.Domain.Ports.IIdempotencyRepository, PaymentGateway.Infrastructure.Repositories.IdempotencyRepository>();
        services.AddTransient<PaymentGateway.Infrastructure.Services.MockSettlementPort>();

        services.Configure<PaymentGateway.Infrastructure.Adyen.AdyenOptions>(configuration.GetSection(PaymentGateway.Infrastructure.Adyen.AdyenOptions.SectionName));
        var adyenOptions = configuration.GetSection(PaymentGateway.Infrastructure.Adyen.AdyenOptions.SectionName).Get<PaymentGateway.Infrastructure.Adyen.AdyenOptions>() ?? new PaymentGateway.Infrastructure.Adyen.AdyenOptions();

        var isLiveAdyen = string.Equals(adyenOptions.Mode, "Live", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(adyenOptions.ApiKey);
        if (!isLiveAdyen)
        {
            services.AddSingleton<PaymentGateway.Infrastructure.Adyen.AdyenWireMockServer>();
            services.AddHostedService(sp => sp.GetRequiredService<PaymentGateway.Infrastructure.Adyen.AdyenWireMockServer>());
        }

        services.AddHttpClient<PaymentGateway.Infrastructure.Adyen.AdyenSettlementPort>((sp, client) =>
        {
            client.Timeout = TimeSpan.FromSeconds(adyenOptions.TimeoutSeconds > 0 ? adyenOptions.TimeoutSeconds : 5);
        });

        services.AddScoped<PaymentGateway.Domain.Ports.ISettlementPort>(sp =>
        {
            var activeChannel = configuration["PaymentGateway:ActiveChannel"];
            if (string.Equals(activeChannel, "MOCK", StringComparison.OrdinalIgnoreCase))
            {
                return sp.GetRequiredService<PaymentGateway.Infrastructure.Services.MockSettlementPort>();
            }

            return sp.GetRequiredService<PaymentGateway.Infrastructure.Adyen.AdyenSettlementPort>();
        });

        services.Configure<PaymentGateway.Infrastructure.Options.OutboxOptions>(configuration.GetSection(PaymentGateway.Infrastructure.Options.OutboxOptions.SectionName));
        services.AddSingleton<PaymentGateway.Infrastructure.Events.ITestEventStore, PaymentGateway.Infrastructure.Events.InMemoryTestEventStore>();
        services.AddHostedService<PaymentGateway.Infrastructure.Services.OutboxDispatcherService>();

        var useInMemory = configuration.GetValue("RabbitMq:UseInMemory", true);
        var rabbitHost = configuration["RabbitMq:Host"] ?? "localhost";
        var rabbitUser = configuration["RabbitMq:Username"] ?? "guest";
        var rabbitPass = configuration["RabbitMq:Password"] ?? "guest";

        services.AddMassTransit(x =>
        {
            x.AddConsumer<PaymentGateway.Infrastructure.Events.PaymentLifecycleTestConsumer>();

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
