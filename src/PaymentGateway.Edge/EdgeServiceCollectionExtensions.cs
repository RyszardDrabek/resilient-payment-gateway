using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace PaymentGateway.Edge;

/// <summary>
/// EDGE HTTP plumbing stubs (ADR-007). Problem Details registry, rate limits, and masking land in F-EDGE-*.
/// </summary>
public static class EdgeServiceCollectionExtensions
{
    public static IServiceCollection AddEdge(this IServiceCollection services)
    {
        services.AddProblemDetails();
        return services;
    }

    public static IApplicationBuilder UseEdge(this IApplicationBuilder app)
    {
        // Full RFC 7807 exception mapping lands in F-EDGE-01.
        app.UseStatusCodePages();
        return app;
    }
}
