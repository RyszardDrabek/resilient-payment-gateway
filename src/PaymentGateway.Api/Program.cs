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

var authority = builder.Configuration["Auth:Authority"];
var jwtKey = builder.Configuration["Auth:JwtSigningKey"];

if (string.IsNullOrWhiteSpace(authority) && string.IsNullOrWhiteSpace(jwtKey))
{
    throw new InvalidOperationException("Either Auth:Authority or Auth:JwtSigningKey must be configured.");
}

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = "SmartScheme";
        options.DefaultChallengeScheme = "SmartScheme";
    })
    .AddPolicyScheme("SmartScheme", "SmartScheme", options =>
    {
        options.ForwardDefaultSelector = context =>
        {
            if (context.Request.Headers.ContainsKey(PaymentGateway.Api.Auth.ApiKeyDefaults.HeaderName))
            {
                return PaymentGateway.Api.Auth.ApiKeyDefaults.AuthenticationScheme;
            }
            return JwtBearerDefaults.AuthenticationScheme;
        };
    })
    .AddScheme<PaymentGateway.Api.Auth.ApiKeyAuthenticationOptions, PaymentGateway.Api.Auth.ApiKeyAuthenticationHandler>(
        PaymentGateway.Api.Auth.ApiKeyDefaults.AuthenticationScheme, _ => { })
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        if (!string.IsNullOrWhiteSpace(authority))
        {
            options.Authority = authority;
            options.RequireHttpsMetadata = builder.Configuration.GetValue<bool>("Auth:RequireHttpsMetadata", false);

            var defaultAudience = builder.Configuration["Auth:Audience"] ?? "payment-gateway";
            var configuredAudiences = builder.Configuration.GetSection("Auth:ValidAudiences").Get<string[]>() ?? [];
            var allValidAudiences = new HashSet<string>(configuredAudiences, StringComparer.OrdinalIgnoreCase)
            {
                defaultAudience,
                "payment-gateway-portal",
                "account"
            };

            var defaultIssuer = builder.Configuration["Auth:Issuer"] ?? "payment-gateway";
            var configuredIssuers = builder.Configuration.GetSection("Auth:ValidIssuers").Get<string[]>() ?? [];
            var allValidIssuers = new HashSet<string>(configuredIssuers, StringComparer.OrdinalIgnoreCase)
            {
                authority,
                defaultIssuer
            };

            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuers = allValidIssuers,
                ValidateAudience = true,
                ValidAudiences = allValidAudiences,
                ValidateLifetime = true,
                RoleClaimType = "role",
                NameClaimType = "preferred_username"
            };

            if (!string.IsNullOrWhiteSpace(jwtKey))
            {
                var symmetricKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
                options.TokenValidationParameters.IssuerSigningKeyResolver = (token, securityToken, kid, validationParameters) =>
                {
                    if (securityToken is Microsoft.IdentityModel.JsonWebTokens.JsonWebToken jsonWebToken &&
                        jsonWebToken.Alg == SecurityAlgorithms.HmacSha256)
                    {
                        return [symmetricKey];
                    }

                    if (securityToken is System.IdentityModel.Tokens.Jwt.JwtSecurityToken jwtSecurityToken &&
                        jwtSecurityToken.Header.Alg == SecurityAlgorithms.HmacSha256)
                    {
                        return [symmetricKey];
                    }

                    var openIdConfig = options.ConfigurationManager?.GetConfigurationAsync(CancellationToken.None).GetAwaiter().GetResult();
                    return openIdConfig?.SigningKeys ?? [];
                };
            }
        }
        else
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = builder.Configuration["Auth:Issuer"] ?? "payment-gateway",
                ValidAudience = builder.Configuration["Auth:Audience"] ?? "payment-gateway",
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey!)),
                RoleClaimType = "role"
            };
        }

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
        policy.RequireAssertion(ctx => HasRole(ctx.User, "merchant", "risk", "ops"));
    });

    options.AddPolicy("MerchantWebhookPolicy", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(ctx => HasRole(ctx.User, "merchant", "ops"));
    });

    options.AddPolicy("RiskPolicy", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(ctx => HasRole(ctx.User, "risk", "ops"));
    });

    options.AddPolicy("ObservabilityPolicy", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(ctx => HasRole(ctx.User, "ops", "merchant", "risk"));
    });
});

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("PaymentGateway.Api"))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddSource(PaymentGateway.Application.Common.PaymentTracing.ActivitySourceName)
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
    var lockProvider = scope.ServiceProvider.GetRequiredService<PaymentGateway.Application.Locking.IDistributedLockProvider>();
    app.Logger.LogInformation("Attempting to acquire migration advisory lock...");

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    PaymentGateway.Application.Locking.IDistributedLock? migrationLock = null;
    while (!cts.IsCancellationRequested && migrationLock is null)
    {
        migrationLock = await lockProvider.TryAcquireLockAsync("DatabaseMigration", cts.Token);
        if (migrationLock is null)
        {
            app.Logger.LogInformation("Database migration lock held by another replica. Waiting...");
            await Task.Delay(1000, cts.Token);
        }
    }

    if (migrationLock is not null)
    {
        await using (migrationLock)
        {
            app.Logger.LogInformation("Acquired database migration lock. Running migrations...");
            var payDb = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            await payDb.Database.MigrateAsync();
            var riskDb = scope.ServiceProvider.GetRequiredService<RiskDbContext>();
            await riskDb.Database.MigrateAsync();
            app.Logger.LogInformation("Database migrations completed successfully.");
        }
    }
    else
    {
        app.Logger.LogWarning("Timed out waiting for migration lock. Skipping migration execution on this instance.");
    }
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
app.MapPaymentMethodEndpoints();
app.MapSubscriptionEndpoints();
app.MapReconciliationEndpoints();
app.MapPayoutEndpoints();
app.MapUnresolvedPaymentEndpoints();
app.MapAdyenWebhookEndpoints();
app.MapOpsReconciliationEndpoints();
app.MapOpsWeb3Endpoints();
app.MapRiskEndpoints();
app.MapMerchantWebhookEndpoints();
app.MapMerchantApiKeyEndpoints();
app.MapMerchantWebhookDlqEndpoints();
app.MapOpsObservabilityEndpoints();
app.MapFxEndpoints();
app.MapGet("/healthz", () => Results.Ok(new { service = "PaymentGateway.Api", status = "Healthy" })).AllowAnonymous();
app.MapGet("/health", async (
    PaymentDbContext db,
    RiskDbContext riskDb,
    TimeProvider timeProvider,
    Microsoft.Extensions.Options.IOptions<PaymentGateway.Infrastructure.Options.OutboxOptions> outboxOptions,
    ILoggerFactory loggerFactory,
    MassTransit.IBusControl bus,
    CancellationToken ct) =>
{
    var canConnectPay = await db.Database.CanConnectAsync(ct);
    var canConnectRisk = await riskDb.Database.CanConnectAsync(ct);
    if (!canConnectPay || !canConnectRisk)
    {
        return Results.Json(new { status = "Unhealthy", error = "Database connection failed" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var busHealth = bus.CheckHealth();
    if (busHealth.Status == MassTransit.BusHealthStatus.Unhealthy)
    {
        return Results.Json(new { status = "Unhealthy", error = $"Message bus unhealthy: {busHealth.Description}" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    try
    {
        var now = timeProvider.GetUtcNow();
        var maxDeliveryAttempts = outboxOptions.Value.MaxDeliveryAttempts > 0 ? outboxOptions.Value.MaxDeliveryAttempts : 5;

        var oldestPendingCreatedAt = await db.OutboxMessages
            .Where(m => m.ProcessedAt == null && m.DeliveryAttempts < maxDeliveryAttempts)
            .OrderBy(m => m.CreatedAt)
            .Select(m => (DateTimeOffset?)m.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var lagSeconds = oldestPendingCreatedAt.HasValue ? Math.Round((now - oldestPendingCreatedAt.Value).TotalSeconds, 1) : 0.0;
        if (lagSeconds > 300 || busHealth.Status == MassTransit.BusHealthStatus.Degraded)
        {
            return Results.Json(new { status = "Degraded", outboxLagSeconds = lagSeconds }, statusCode: StatusCodes.Status200OK);
        }

        return Results.Ok(new { status = "Healthy", outboxLagSeconds = lagSeconds });
    }
    catch (Exception ex)
    {
        var logger = loggerFactory.CreateLogger("Health");
        logger.LogWarning(ex, "Failed to compute outbox lag in health check; falling back to Healthy.");
        return Results.Ok(new { status = "Healthy" });
    }
}).AllowAnonymous();

app.Run();

static bool HasRole(System.Security.Claims.ClaimsPrincipal user, params string[] allowedRoles)
{
    return user.Claims.Any(c =>
    {
        if (c.Type is "role" or System.Security.Claims.ClaimTypes.Role or "roles")
        {
            return allowedRoles.Any(r => string.Equals(c.Value, r, StringComparison.OrdinalIgnoreCase));
        }

        if (c.Type == "realm_access" && !string.IsNullOrWhiteSpace(c.Value))
        {
            return allowedRoles.Any(r => c.Value.Contains($"\"{r}\"", StringComparison.OrdinalIgnoreCase));
        }

        return false;
    });
}

public partial class Program;
