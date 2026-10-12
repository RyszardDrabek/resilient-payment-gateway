using System.Text.Json;
using MediatR;
using PaymentGateway.Application.Payments;
using PaymentGateway.Edge.Problems;
using PaymentGateway.Edge.RateLimiting;

namespace PaymentGateway.Api.Endpoints;

public static class UnresolvedPaymentEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IEndpointRouteBuilder MapUnresolvedPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var handler = async (
            HttpContext httpContext,
            ISender mediator,
            string? channel,
            int? ageMinutes,
            int? page,
            int? pageSize,
            CancellationToken ct) =>
        {
            if (ageMinutes.HasValue && ageMinutes.Value < 0)
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status400BadRequest,
                    PaymentProblemTypes.Validation,
                    "Validation Failed",
                    "ageMinutes cannot be negative.");
            }

            if (page.HasValue && page.Value < 1)
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status400BadRequest,
                    PaymentProblemTypes.Validation,
                    "Validation Failed",
                    "page must be greater than or equal to 1.");
            }

            if (pageSize.HasValue && pageSize.Value < 1)
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status400BadRequest,
                    PaymentProblemTypes.Validation,
                    "Validation Failed",
                    "pageSize must be greater than or equal to 1.");
            }

            var query = new GetUnresolvedPaymentsQuery(
                Channel: channel,
                AgeMinutes: ageMinutes,
                Page: page ?? 1,
                PageSize: pageSize ?? 50);

            var result = await mediator.Send(query, ct);
            return Results.Ok(result);
        };

        app.MapGet("/api/v1/payments/unresolved", handler)
            .WithTags("Payments - Unresolved")
            .RequireAuthorization("UnresolvedPaymentsPolicy")
            .RequireRateLimiting(PaymentRateLimitingExtensions.PolicyName);

        app.MapGet("/payments/unresolved", handler)
            .WithTags("Payments - Unresolved")
            .RequireAuthorization("UnresolvedPaymentsPolicy")
            .RequireRateLimiting(PaymentRateLimitingExtensions.PolicyName);

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

        return Results.Json(problem, JsonOptions, "application/problem+json", statusCode);
    }
}
