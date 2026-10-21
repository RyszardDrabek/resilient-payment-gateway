using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PaymentGateway.Application;
using PaymentGateway.Edge.Telemetry;
using PaymentGateway.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("PaymentGateway.Worker"))
    .WithTracing(t => t
        .AddSource(PaymentGateway.Application.Common.PaymentTracing.ActivitySourceName)
        .AddSource("MassTransit")
        .AddSource("Npgsql")
        .AddSource("System.Net.Http")
        .AddInstrumentMasking()
        .AddOtlpExporter())
    .WithLogging(logging =>
    {
        logging.AddInstrumentMasking();
        logging.AddOtlpExporter(options =>
        {
            var endpoint = builder.Configuration["OTEL_EXPORTER_OTLP_LOGS_ENDPOINT"];
            if (!string.IsNullOrEmpty(endpoint))
            {
                options.Endpoint = new Uri(endpoint);
                options.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
            }
        });
    }, options =>
    {
        options.IncludeFormattedMessage = true;
        options.IncludeScopes = true;
    });

builder.Services.AddHostedService<PaymentGateway.Worker.Web3ConfirmationWatcherWorker>();
builder.Services.AddHostedService<PaymentGateway.Worker.OutboxRetentionCleanupWorker>();
builder.Services.AddHostedService<PaymentGateway.Worker.IdempotencyRetentionCleanupWorker>();
builder.Services.AddHostedService<PaymentGateway.Worker.ThreeDsExpirationWorker>();
builder.Services.AddHostedService<PaymentGateway.Worker.SubscriptionBillingWorker>();
builder.Services.AddHostedService<PaymentGateway.Worker.DailyReconciliationWorker>();
builder.Services.AddHostedService<PaymentGateway.Worker.FxQuoteExpirationWorker>();
builder.Services.AddHostedService<PaymentGateway.Worker.MerchantWebhookRetentionCleanupWorker>();

var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("PaymentGateway.Worker");
logger.LogInformation("PaymentGateway.Worker starting");
await host.RunAsync();
