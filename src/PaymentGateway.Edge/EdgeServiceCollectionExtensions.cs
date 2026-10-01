using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PaymentGateway.Edge.Middleware;
using PaymentGateway.Edge.RateLimiting;

namespace PaymentGateway.Edge;

/// <summary>
/// EDGE HTTP plumbing (ADR-007): Problem Details, exception mapping, rate limits, and masking.
/// </summary>
public static class EdgeServiceCollectionExtensions
{
    public static IServiceCollection AddEdge(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        services.AddProblemDetails();
        services.AddPaymentRateLimiting(configuration);
        return services;
    }

    public static IApplicationBuilder UseEdge(this IApplicationBuilder app)
    {
        // F-EDGE-01: Global RFC 7807 exception handler — must be first so all downstream exceptions are caught.
        app.UseMiddleware<PaymentExceptionHandlerMiddleware>();
        app.UseStatusCodePages();

        return app;
    }
}
