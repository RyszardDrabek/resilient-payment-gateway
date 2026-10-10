using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PaymentGateway.Application;
using PaymentGateway.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("PaymentGateway.Worker"))
    .WithTracing(t => t
        .AddSource("MassTransit")
        .AddSource("Npgsql")
        .AddSource("System.Net.Http")
        .AddOtlpExporter())
    .WithLogging(l => l
        .AddOtlpExporter());

builder.Services.AddHostedService<PaymentGateway.Worker.WorkerHeartbeat>();
builder.Services.AddHostedService<PaymentGateway.Worker.Web3ConfirmationWatcherWorker>();

var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("PaymentGateway.Worker");
logger.LogInformation("PaymentGateway.Worker starting");
await host.RunAsync();
