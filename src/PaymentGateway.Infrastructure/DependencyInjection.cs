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
        services.AddSingleton<PaymentGateway.Application.Adyen.IAdyenHmacValidator, PaymentGateway.Infrastructure.Adyen.AdyenHmacValidator>();
        services.AddScoped<PaymentGateway.Application.Adyen.IAdyenNotificationRepository, PaymentGateway.Infrastructure.Repositories.AdyenNotificationRepository>();
        services.AddTransient<PaymentGateway.Infrastructure.Services.MockSettlementPort>();

        services.Configure<PaymentGateway.Infrastructure.Adyen.AdyenOptions>(configuration.GetSection(PaymentGateway.Infrastructure.Adyen.AdyenOptions.SectionName));
        var adyenOptions = configuration.GetSection(PaymentGateway.Infrastructure.Adyen.AdyenOptions.SectionName).Get<PaymentGateway.Infrastructure.Adyen.AdyenOptions>() ?? new PaymentGateway.Infrastructure.Adyen.AdyenOptions();

        if (!adyenOptions.IsLiveMode)
        {
            services.AddSingleton<PaymentGateway.Infrastructure.Adyen.AdyenWireMockServer>();
            services.AddHostedService(sp => sp.GetRequiredService<PaymentGateway.Infrastructure.Adyen.AdyenWireMockServer>());
        }

        services.AddHttpClient<PaymentGateway.Infrastructure.Adyen.AdyenSettlementPort>((sp, client) =>
        {
            client.Timeout = TimeSpan.FromSeconds(adyenOptions.TimeoutSeconds > 0 ? adyenOptions.TimeoutSeconds : 5);
        });

        services.Configure<PaymentGateway.Infrastructure.Web3.Web3Options>(configuration.GetSection(PaymentGateway.Infrastructure.Web3.Web3Options.SectionName));
        services.AddSingleton<PaymentGateway.Infrastructure.Web3.IWeb3ChainClient, PaymentGateway.Infrastructure.Web3.LocalWeb3SimulatorClient>();
        services.AddScoped<PaymentGateway.Infrastructure.Web3.Web3SettlementPort>();
        services.AddScoped<PaymentGateway.Application.Web3.IWeb3FinalityWatcherService, PaymentGateway.Infrastructure.Web3.Web3FinalityWatcherService>();

        services.AddScoped<PaymentGateway.Infrastructure.Services.RoutingSettlementPort>();
        services.AddScoped<PaymentGateway.Domain.Ports.ISettlementPort>(sp =>
            sp.GetRequiredService<PaymentGateway.Infrastructure.Services.RoutingSettlementPort>());

        services.Configure<PaymentGateway.Infrastructure.Options.OutboxOptions>(configuration.GetSection(PaymentGateway.Infrastructure.Options.OutboxOptions.SectionName));
        services.AddSingleton<PaymentGateway.Infrastructure.Events.ITestEventStore, PaymentGateway.Infrastructure.Events.InMemoryTestEventStore>();
        services.AddHostedService<PaymentGateway.Infrastructure.Services.OutboxDispatcherService>();

        var useInMemory = configuration.GetValue("RabbitMq:UseInMemory", true);
        var rabbitHost = configuration["RabbitMq:Host"] ?? "localhost";
        var rabbitUser = configuration["RabbitMq:Username"] ?? "guest";
        var rabbitPass = configuration["RabbitMq:Password"] ?? "guest";

        services.Configure<PaymentGateway.Infrastructure.Risk.RiskOptions>(configuration.GetSection(PaymentGateway.Infrastructure.Risk.RiskOptions.SectionName));
        services.AddScoped<PaymentGateway.Application.Risk.Ports.IRiskVerdictRepository, PaymentGateway.Infrastructure.Repositories.RiskVerdictRepository>();
        services.AddScoped<PaymentGateway.Application.Risk.Ports.IRiskFlagRepository, PaymentGateway.Infrastructure.Repositories.RiskFlagRepository>();
        services.AddScoped<PaymentGateway.Application.Risk.Ports.IRiskContextStore, PaymentGateway.Infrastructure.Repositories.PartyRiskContextStore>();
        services.AddScoped<PaymentGateway.Application.Risk.Ports.IRiskScorer, PaymentGateway.Infrastructure.Risk.DeterministicRiskScorer>();

        services.AddMassTransit(x =>
        {
            x.AddConsumer<PaymentGateway.Infrastructure.Events.PaymentLifecycleTestConsumer>();
            x.AddConsumer<PaymentGateway.Infrastructure.Consumers.PaymentLifecycleRiskConsumer>();

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

        services.Configure<MassTransitHostOptions>(options =>
        {
            options.WaitUntilStarted = true;
            options.StartTimeout = TimeSpan.FromSeconds(30);
            options.StopTimeout = TimeSpan.FromSeconds(30);
        });

        return services;
    }
}
