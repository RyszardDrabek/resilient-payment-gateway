using MediatR;
using PaymentGateway.Application.Risk.Queries;
using PaymentGateway.Edge.Problems;

namespace PaymentGateway.Api.Endpoints;

public static class RiskEndpoints
{
    public static IEndpointRouteBuilder MapRiskEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/risk")
            .WithTags("Risk")
            .RequireAuthorization();

        group.MapGet("/verdicts/{id}", async (string id, ISender mediator, HttpContext httpContext, CancellationToken ct) =>
        {
            var verdict = await mediator.Send(new GetRiskVerdictQuery(id), ct);
            if (verdict is null)
            {
                var correlationId = httpContext.TraceIdentifier;
                if (!httpContext.Response.Headers.ContainsKey("X-Correlation-Id"))
                {
                    httpContext.Response.Headers["X-Correlation-Id"] = correlationId;
                }

                var problem = new PaymentProblemDetails(
                    Type: PaymentProblemTypes.NotFound,
                    Title: "Risk Verdict Not Found",
                    Status: StatusCodes.Status404NotFound,
                    Detail: $"No risk verdict found matching identifier '{id}'.",
                    CorrelationId: correlationId);

                return Results.Json(problem, statusCode: StatusCodes.Status404NotFound, contentType: "application/problem+json");
            }

            return Results.Ok(verdict);
        });

        return app;
    }
}
