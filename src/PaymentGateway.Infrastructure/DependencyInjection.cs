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

        services.AddSingleton<DomainEventsToOutboxInterceptor>();
        services.AddDbContext<PaymentDbContext>((sp, options) =>
        {
            var cs = sp.GetRequiredService<IConfiguration>().GetConnectionString("PaymentDb")
                ?? throw new InvalidOperationException("Connection string 'PaymentDb' is required.");
            options.UseNpgsql(cs, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "pay"));
            options.AddInterceptors(sp.GetRequiredService<DomainEventsToOutboxInterceptor>());
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
        services.AddScoped<PaymentGateway.Domain.Ports.IUnitOfWork, PaymentGateway.Infrastructure.Persistence.UnitOfWork>();
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

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PaymentGateway.Infrastructure.Adyen.AdyenOptions>>().Value;
            var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<PaymentGateway.Infrastructure.Adyen.AdyenSettlementPort>>();
            return PaymentGateway.Infrastructure.Adyen.AdyenSettlementPort.CreatePipeline(options, logger);
        });

        services.AddHttpClient<PaymentGateway.Infrastructure.Adyen.AdyenSettlementPort>((sp, client) =>
        {
            client.Timeout = TimeSpan.FromSeconds(adyenOptions.TimeoutSeconds > 0 ? adyenOptions.TimeoutSeconds : 5);
        });

        services.Configure<PaymentGateway.Infrastructure.Web3.Web3Options>(configuration.GetSection(PaymentGateway.Infrastructure.Web3.Web3Options.SectionName));
        services.AddSingleton<PaymentGateway.Infrastructure.Web3.IWeb3ChainClient, PaymentGateway.Infrastructure.Web3.LocalWeb3SimulatorClient>();
        services.AddScoped<PaymentGateway.Infrastructure.Web3.Web3SettlementPort>();
        services.AddScoped<PaymentGateway.Application.Web3.IWeb3FinalityWatcherService, PaymentGateway.Infrastructure.Web3.Web3FinalityWatcherService>();

        services.AddScoped<PaymentGateway.Infrastructure.Services.RoutingSettlementPort>(sp =>
            new PaymentGateway.Infrastructure.Services.RoutingSettlementPort(sp, configuration));
        services.AddScoped<PaymentGateway.Domain.Ports.ISettlementPort>(sp =>
            sp.GetRequiredService<PaymentGateway.Infrastructure.Services.RoutingSettlementPort>());

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<PaymentGateway.Application.Locking.IDistributedLockProvider, PaymentGateway.Infrastructure.Locking.PostgresAdvisoryLockProvider>();
        services.Configure<PaymentGateway.Infrastructure.Options.OutboxOptions>(configuration.GetSection(PaymentGateway.Infrastructure.Options.OutboxOptions.SectionName));
        services.AddScoped<PaymentGateway.Application.Events.IOutboxRetentionService, PaymentGateway.Infrastructure.Services.OutboxRetentionService>();
        services.Configure<PaymentGateway.Infrastructure.Options.IdempotencyRetentionOptions>(configuration.GetSection(PaymentGateway.Infrastructure.Options.IdempotencyRetentionOptions.SectionName));
        services.AddScoped<PaymentGateway.Application.Idempotency.IIdempotencyRetentionService, PaymentGateway.Infrastructure.Services.IdempotencyRetentionService>();
        services.AddSingleton<PaymentGateway.Infrastructure.Events.ITestEventStore, PaymentGateway.Infrastructure.Events.InMemoryTestEventStore>();
        services.AddHostedService<PaymentGateway.Infrastructure.Services.OutboxDispatcherService>();

        var useInMemory = configuration.GetValue("RabbitMq:UseInMemory", true);
        var rabbitHost = configuration["RabbitMq:Host"] ?? "localhost";
        var rabbitUser = configuration["RabbitMq:Username"] ?? "guest";
        var rabbitPass = configuration["RabbitMq:Password"] ?? "guest";

        services.Configure<PaymentGateway.Infrastructure.Risk.RiskOptions>(configuration.GetSection(PaymentGateway.Infrastructure.Risk.RiskOptions.SectionName));
        services.Configure<PaymentGateway.Infrastructure.Risk.InlineRiskOptions>(configuration.GetSection(PaymentGateway.Infrastructure.Risk.InlineRiskOptions.SectionName));
        services.AddScoped<PaymentGateway.Application.Risk.Ports.IRiskVerdictRepository, PaymentGateway.Infrastructure.Repositories.RiskVerdictRepository>();
        services.AddScoped<PaymentGateway.Application.Risk.Ports.IRiskFlagRepository, PaymentGateway.Infrastructure.Repositories.RiskFlagRepository>();
        services.AddScoped<PaymentGateway.Application.Risk.Ports.IRiskContextStore, PaymentGateway.Infrastructure.Repositories.PartyRiskContextStore>();
        services.AddScoped<PaymentGateway.Application.Risk.Ports.IRiskScorer, PaymentGateway.Infrastructure.Risk.DeterministicRiskScorer>();
        services.AddScoped<PaymentGateway.Application.Risk.Ports.IInlineRiskEvaluator, PaymentGateway.Infrastructure.Risk.InlineRiskEvaluator>();

        services.Configure<PaymentGateway.Infrastructure.Options.MerchantWebhookOptions>(
            configuration.GetSection(PaymentGateway.Infrastructure.Options.MerchantWebhookOptions.SectionName));
        services.AddSingleton<PaymentGateway.Application.Webhooks.IMerchantWebhookSigner, PaymentGateway.Application.Webhooks.HmacSha256WebhookSigner>();
        services.AddScoped<PaymentGateway.Domain.Ports.IMerchantWebhookSubscriptionRepository, PaymentGateway.Infrastructure.Repositories.MerchantWebhookSubscriptionRepository>();
        services.AddScoped<PaymentGateway.Domain.Ports.IMerchantApiKeyRepository, PaymentGateway.Infrastructure.Repositories.MerchantApiKeyRepository>();
        services.AddScoped<PaymentGateway.Domain.Ports.IMerchantWebhookDeadLetterRepository, PaymentGateway.Infrastructure.Repositories.MerchantWebhookDeadLetterRepository>();
        services.AddScoped<PaymentGateway.Application.Webhooks.IMerchantWebhookRetentionService, PaymentGateway.Infrastructure.Services.MerchantWebhookRetentionService>();
        services.AddScoped<PaymentGateway.Domain.Ports.IPaymentMethodRepository, PaymentGateway.Infrastructure.Repositories.PaymentMethodRepository>();
        services.AddScoped<PaymentGateway.Domain.Ports.ISubscriptionRepository, PaymentGateway.Infrastructure.Repositories.SubscriptionRepository>();
        services.AddScoped<PaymentGateway.Domain.Ports.IReconciliationRepository, PaymentGateway.Infrastructure.Repositories.ReconciliationRepository>();
        services.AddScoped<PaymentGateway.Domain.Ports.IFeeScheduleRepository, PaymentGateway.Infrastructure.Repositories.FeeScheduleRepository>();
        services.AddScoped<PaymentGateway.Domain.Ports.IPayoutRepository, PaymentGateway.Infrastructure.Repositories.PayoutRepository>();
        services.AddHttpClient();
        services.AddHttpClient<PaymentGateway.Application.Webhooks.IMerchantWebhookDispatcher, PaymentGateway.Infrastructure.Webhooks.MerchantWebhookDispatcher>();
        services.AddHttpClient<PaymentGateway.Application.Webhooks.IMerchantWebhookSimulator, PaymentGateway.Infrastructure.Webhooks.MerchantWebhookSimulator>();
        services.AddHttpClient<PaymentGateway.Application.Webhooks.ReplayMerchantWebhookCommandHandler>();
        services.AddScoped<PaymentGateway.Application.Observability.IObservabilityService, PaymentGateway.Infrastructure.Services.ObservabilityService>();
        services.Configure<PaymentGateway.Application.Fx.Models.FxOptions>(configuration.GetSection(PaymentGateway.Application.Fx.Models.FxOptions.SectionName));
        services.Configure<PaymentGateway.Infrastructure.Options.FxQuoteExpirationOptions>(
            configuration.GetSection(PaymentGateway.Infrastructure.Options.FxQuoteExpirationOptions.SectionName));
        services.Configure<PaymentGateway.Infrastructure.Options.FxRateRefreshOptions>(
            configuration.GetSection(PaymentGateway.Infrastructure.Options.FxRateRefreshOptions.SectionName));
        services.AddHttpClient<PaymentGateway.Application.Fx.Ports.IExternalFxFeedClient, PaymentGateway.Infrastructure.Fx.NbpFxFeedClient>(client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ResilientPaymentGateway/1.0");
        });
        services.AddSingleton<PaymentGateway.Infrastructure.Fx.CachedFxRateProvider>();
        services.AddSingleton<PaymentGateway.Application.Fx.Ports.IFxRateProvider>(sp => sp.GetRequiredService<PaymentGateway.Infrastructure.Fx.CachedFxRateProvider>());
        services.AddScoped<PaymentGateway.Domain.Ports.IFxQuoteRepository, PaymentGateway.Infrastructure.Repositories.FxQuoteRepository>();
        services.AddScoped<PaymentGateway.Application.Fx.IFxQuoteExpirationService, PaymentGateway.Infrastructure.Services.FxQuoteExpirationService>();

        services.AddMassTransit(x =>
        {
            x.AddBusObserver<PaymentGateway.Infrastructure.Services.MassTransitReadyBusObserver>();
            x.AddConsumer<PaymentGateway.Infrastructure.Events.PaymentLifecycleTestConsumer>();
            x.AddConsumer<PaymentGateway.Infrastructure.Consumers.PaymentLifecycleRiskConsumer>();
            x.AddConsumer<PaymentGateway.Infrastructure.Consumers.MerchantWebhookDispatcherConsumer>();
            x.AddConsumer<PaymentGateway.Infrastructure.Consumers.MerchantWebhookDeadLetterConsumer>();
            x.AddConsumer<PaymentGateway.Infrastructure.Consumers.FxRatesUpdatedConsumer>();

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
