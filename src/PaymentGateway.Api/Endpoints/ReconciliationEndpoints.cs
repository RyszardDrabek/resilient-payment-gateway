using MediatR;
using PaymentGateway.Application.Reconciliation;

namespace PaymentGateway.Api.Endpoints;

public record RunReconciliationRequest(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null);

public record ResolveDiscrepancyRequest(
    string? ResolutionNotes = null);

public static class ReconciliationEndpoints
{
    public static IEndpointRouteBuilder MapReconciliationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/reconciliation")
            .WithTags("Reconciliation")
            .RequireAuthorization();

        group.MapGet("/summary", async (
            DateTimeOffset? from,
            DateTimeOffset? to,
            ISender mediator,
            CancellationToken ct) =>
        {
            var summary = await mediator.Send(new GetReconciliationSummaryQuery(from, to), ct);
            return Results.Ok(summary);
        });

        group.MapGet("/discrepancies", async (
            int? skip,
            int? take,
            ISender mediator,
            CancellationToken ct) =>
        {
            var discrepancies = await mediator.Send(new ListDiscrepanciesQuery(skip ?? 0, take ?? 50), ct);
            return Results.Ok(discrepancies);
        });

        group.MapPost("/run", async (
            RunReconciliationRequest request,
            ISender mediator,
            CancellationToken ct) =>
        {
            var to = request.To ?? DateTimeOffset.UtcNow;
            var from = request.From ?? to.AddDays(-1);

            var result = await mediator.Send(new RunReconciliationCommand(from, to), ct);
            return Results.Ok(result);
        });

        group.MapPost("/discrepancies/{id}/resolve", async (
            string id,
            ResolveDiscrepancyRequest request,
            ISender mediator,
            CancellationToken ct) =>
        {
            var success = await mediator.Send(new ResolveDiscrepancyCommand(id, request.ResolutionNotes), ct);
            if (!success)
            {
                return Results.NotFound(new { error = $"Discrepancy record '{id}' not found." });
            }

            return Results.Ok(new { message = "Discrepancy marked as resolved." });
        });

        return app;
    }
}
