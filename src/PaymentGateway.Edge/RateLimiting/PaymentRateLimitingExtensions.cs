using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PaymentGateway.Edge.Options;
using PaymentGateway.Edge.Problems;

namespace PaymentGateway.Edge.RateLimiting;

/// <summary>
/// Rate limiting extension helpers and policies for Payment API (ADR-007, F-EDGE-02).
/// Enforces per-caller rate limits and global in-flight concurrency caps with RFC 7807 problem responses.
/// </summary>
public static class PaymentRateLimitingExtensions
{
    public const string PolicyName = "payment-api";
    private const string CorrelationIdHeader = "X-Correlation-Id";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IServiceCollection AddPaymentRateLimiting(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        if (configuration is not null)
        {
            services.Configure<PaymentRateLimitOptions>(
                configuration.GetSection(PaymentRateLimitOptions.SectionName));
        }

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                await HandleRateLimitRejectionAsync(context.HttpContext, cancellationToken);
            };

            // Global concurrency limiter across payment API requests to protect against server overload (AC-3)
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                // Only apply overload protection to endpoints that require payment-api rate limiting
                var endpoint = httpContext.GetEndpoint();
                var requireRateLimiting = endpoint?.Metadata.GetMetadata<EnableRateLimitingAttribute>();
                if (requireRateLimiting is null || requireRateLimiting.PolicyName != PolicyName)
                {
                    return RateLimitPartition.GetNoLimiter("unlimited");
                }

                var optionsMonitor = httpContext.RequestServices
                    .GetService<IOptionsMonitor<PaymentRateLimitOptions>>();
                var rateLimitOptions = optionsMonitor?.CurrentValue ?? new PaymentRateLimitOptions();

                return RateLimitPartition.GetConcurrencyLimiter(
                    "global-payment-concurrency",
                    _ => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = rateLimitOptions.GlobalConcurrencyLimit,
                        QueueLimit = rateLimitOptions.QueueLimit,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    });
            });

            // Per-caller partition policy (AC-1, AC-2)
            options.AddPolicy(PolicyName, httpContext =>
            {
                var optionsMonitor = httpContext.RequestServices
                    .GetService<IOptionsMonitor<PaymentRateLimitOptions>>();
                var rateLimitOptions = optionsMonitor?.CurrentValue ?? new PaymentRateLimitOptions();

                // Key rate limits from coarse caller identity: JWT sub claim first, fallback to IP address, or anonymous
                var partitionKey = ResolveCallerIdentity(httpContext);

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rateLimitOptions.PermitLimitPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = rateLimitOptions.QueueLimit,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    });
            });
        });

        return services;
    }

    public static string ResolveCallerIdentity(HttpContext httpContext)
    {
        var user = httpContext.User;
        var subClaim = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrWhiteSpace(subClaim))
        {
            return $"sub:{subClaim}";
        }

        var ip = httpContext.Connection.RemoteIpAddress?.ToString();
        if (!string.IsNullOrWhiteSpace(ip))
        {
            return $"ip:{ip}";
        }

        return "anonymous";
    }

    public static async Task HandleRateLimitRejectionAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        var correlationId = httpContext.TraceIdentifier;

        if (!httpContext.Response.Headers.ContainsKey(CorrelationIdHeader))
        {
            httpContext.Response.Headers[CorrelationIdHeader] = correlationId;
        }

        var problem = new PaymentProblemDetails(
            Type: PaymentProblemTypes.RateLimit,
            Title: "Too Many Requests",
            Status: StatusCodes.Status429TooManyRequests,
            Detail: "API rate limit or concurrency cap exceeded. Please back off and retry.",
            CorrelationId: correlationId);

        httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        httpContext.Response.ContentType = "application/problem+json";

        var json = JsonSerializer.Serialize(problem, JsonOptions);
        await httpContext.Response.WriteAsync(json, cancellationToken);
    }
}
