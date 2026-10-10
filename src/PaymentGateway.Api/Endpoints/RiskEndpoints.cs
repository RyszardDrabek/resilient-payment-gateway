using MediatR;
using PaymentGateway.Application.Risk.Commands;
using PaymentGateway.Application.Risk.Queries;
using PaymentGateway.Domain.Entities;
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
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status404NotFound,
                    PaymentProblemTypes.NotFound,
                    "Risk Verdict Not Found",
                    $"No risk verdict found matching identifier '{id}'.");
            }

            return Results.Ok(verdict);
        });

        group.MapGet("/flags", async (int? page, int? pageSize, ISender mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new ListOpenRiskFlagsQuery(page ?? 1, pageSize ?? 50), ct);
            return Results.Ok(result);
        });

        group.MapPost("/flags/{id}/disposition", async (string id, DispositionRiskFlagRequest request, ISender mediator, HttpContext httpContext, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request?.Disposition))
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status400BadRequest,
                    PaymentProblemTypes.Validation,
                    "Validation Failed",
                    "Disposition is required and must be one of: confirmed, false_positive, escalated.");
            }

            var normalized = request.Disposition.Trim().ToLowerInvariant();
            if (!RiskFlag.SupportedDispositions.Contains(normalized))
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status400BadRequest,
                    PaymentProblemTypes.Validation,
                    "Validation Failed",
                    $"Unsupported disposition value '{request.Disposition}'. Supported values: confirmed, false_positive, escalated.");
            }

            var commandResult = await mediator.Send(new DispositionRiskFlagCommand(id, normalized, request.Notes), ct);

            return commandResult.Outcome switch
            {
                DispositionOutcome.Success => Results.Ok(commandResult.Flag),
                DispositionOutcome.NotFound => ProblemResult(
                    httpContext,
                    StatusCodes.Status404NotFound,
                    PaymentProblemTypes.NotFound,
                    "Risk Flag Not Found",
                    commandResult.ErrorMessage ?? $"No risk flag found matching identifier '{id}'."),
                DispositionOutcome.AlreadyDispositioned => ProblemResult(
                    httpContext,
                    StatusCodes.Status409Conflict,
                    PaymentProblemTypes.Conflict,
                    "Conflict",
                    commandResult.ErrorMessage ?? $"Risk flag '{id}' has already been dispositioned."),
                DispositionOutcome.InvalidDisposition => ProblemResult(
                    httpContext,
                    StatusCodes.Status400BadRequest,
                    PaymentProblemTypes.Validation,
                    "Validation Failed",
                    commandResult.ErrorMessage ?? "Invalid disposition."),
                _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
            };
        });

        return app;
    }

    private static IResult ProblemResult(
        HttpContext httpContext,
        int statusCode,
        string type,
        string title,
        string detail)
    {
        var correlationId = httpContext.TraceIdentifier;
        if (!httpContext.Response.Headers.ContainsKey("X-Correlation-Id"))
        {
            httpContext.Response.Headers["X-Correlation-Id"] = correlationId;
        }

        var problem = new PaymentProblemDetails(
            Type: type,
            Title: title,
            Status: statusCode,
            Detail: detail,
            CorrelationId: correlationId);

        return Results.Json(problem, statusCode: statusCode, contentType: "application/problem+json");
    }
}
