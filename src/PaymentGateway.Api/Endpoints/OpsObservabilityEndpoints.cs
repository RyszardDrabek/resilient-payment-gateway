using PaymentGateway.Application.Observability;

namespace PaymentGateway.Api.Endpoints;

public static class OpsObservabilityEndpoints
{
    public static IEndpointRouteBuilder MapOpsObservabilityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/ops/observability")
            .WithTags("Ops - Observability")
            .RequireAuthorization("ObservabilityPolicy");

        group.MapGet("/", async (IObservabilityService observabilityService, CancellationToken ct) =>
        {
            var summary = await observabilityService.GetSummaryAsync(ct);
            return Results.Ok(summary);
        });

        return app;
    }
}
