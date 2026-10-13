using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PaymentGateway.Api.Endpoints;
using PaymentGateway.Application;
using PaymentGateway.Edge;
using PaymentGateway.Edge.Telemetry;
using PaymentGateway.Infrastructure;
using PaymentGateway.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddEdge(builder.Configuration);

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:3000", "http://127.0.0.1:3000"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("PortalPolicy", policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .WithExposedHeaders("X-Correlation-Id");
    });
});

var jwtKey = builder.Configuration["Auth:JwtSigningKey"]
    ?? throw new InvalidOperationException("Auth:JwtSigningKey is required.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Auth:Issuer"] ?? "payment-gateway",
            ValidAudience = builder.Configuration["Auth:Audience"] ?? "payment-gateway",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            RoleClaimType = "role"
        };
        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/problem+json";
                var correlationId = context.HttpContext.TraceIdentifier;
                if (!context.Response.Headers.ContainsKey("X-Correlation-Id"))
                {
                    context.Response.Headers["X-Correlation-Id"] = correlationId;
                }
                var problem = new PaymentGateway.Edge.Problems.PaymentProblemDetails(
                    Type: PaymentGateway.Edge.Problems.PaymentProblemTypes.Unauthorized,
                    Title: "Unauthorized",
                    Status: StatusCodes.Status401Unauthorized,
                    Detail: "Authentication token is missing, invalid, or expired.",
                    CorrelationId: correlationId);
                await context.Response.WriteAsJsonAsync(
                    problem,
                    options: (System.Text.Json.JsonSerializerOptions?)null,
                    contentType: "application/problem+json",
                    cancellationToken: context.HttpContext.RequestAborted);
            },
            OnForbidden = async context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/problem+json";
                var correlationId = context.HttpContext.TraceIdentifier;
                if (!context.Response.Headers.ContainsKey("X-Correlation-Id"))
                {
                    context.Response.Headers["X-Correlation-Id"] = correlationId;
                }
                var problem = new PaymentGateway.Edge.Problems.PaymentProblemDetails(
                    Type: PaymentGateway.Edge.Problems.PaymentProblemTypes.Forbidden,
                    Title: "Forbidden",
                    Status: StatusCodes.Status403Forbidden,
                    Detail: "The caller does not have the required role to access this resource.",
                    CorrelationId: correlationId);
                await context.Response.WriteAsJsonAsync(
                    problem,
                    options: (System.Text.Json.JsonSerializerOptions?)null,
                    contentType: "application/problem+json",
                    cancellationToken: context.HttpContext.RequestAborted);
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("UnresolvedPaymentsPolicy", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(ctx =>
            ctx.User.HasClaim(c =>
                (c.Type == "role" || c.Type == System.Security.Claims.ClaimTypes.Role) &&
                (string.Equals(c.Value, "merchant", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(c.Value, "risk", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(c.Value, "ops", StringComparison.OrdinalIgnoreCase))));
    });
});

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("PaymentGateway.Api"))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddSource("MassTransit")
        .AddSource("Npgsql")
        .AddSource("System.Net.Http")
        .AddInstrumentMasking()    // F-EDGE-03: mask PAN/token/IBAN before export
        .AddOtlpExporter())
    .WithLogging(logging =>
    {
        logging.AddInstrumentMasking(); // F-EDGE-03: mask PAN/token/IBAN in logs before export
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
    })
    .WithMetrics(m => m
        .AddMeter("Microsoft.AspNetCore.Hosting")
        .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
        .AddMeter("PaymentGateway")
        .AddPrometheusExporter());

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor |
                               Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.Logger.LogInformation("PaymentGateway.Api starting");

if (builder.Configuration.GetValue<bool>("Database:AutoMigrate", false) ||
    builder.Configuration.GetValue<bool>("DATABASE_AUTOMIGRATE", false))
{
    using var scope = app.Services.CreateScope();
    var payDb = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
    await payDb.Database.MigrateAsync();
    var riskDb = scope.ServiceProvider.GetRequiredService<RiskDbContext>();
    await riskDb.Database.MigrateAsync();
}

app.UseForwardedHeaders();
app.UseCors("PortalPolicy");
app.UseEdge();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapGet("/", () => Results.Ok(new { service = "PaymentGateway.Api", status = "ok" }));
app.MapPrometheusScrapingEndpoint().AllowAnonymous();
app.MapPaymentEndpoints();
app.MapUnresolvedPaymentEndpoints();
app.MapAdyenWebhookEndpoints();
app.MapOpsReconciliationEndpoints();
app.MapOpsWeb3Endpoints();
app.MapRiskEndpoints();
app.MapGet("/health", async (PaymentDbContext db, RiskDbContext riskDb, CancellationToken ct) =>
{
    var canConnectPay = await db.Database.CanConnectAsync(ct);
    var canConnectRisk = await riskDb.Database.CanConnectAsync(ct);
    return (canConnectPay && canConnectRisk)
        ? Results.Ok(new { status = "Healthy" })
        : Results.Json(new { status = "Unhealthy" }, statusCode: StatusCodes.Status503ServiceUnavailable);
}).AllowAnonymous();

app.Run();

public partial class Program;
