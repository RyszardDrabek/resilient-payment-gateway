using System.Text.Json;
using MediatR;
using PaymentGateway.Application.Adyen;
using PaymentGateway.Edge.Problems;

namespace PaymentGateway.Api.Endpoints;

public static class AdyenWebhookEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IEndpointRouteBuilder MapAdyenWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        var handler = async (HttpContext httpContext, AdyenWebhookPayload payload, ISender mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new IngestAdyenWebhookCommand(payload), ct);

            if (result.IsSuccess)
            {
                return Results.Text("[accepted]", "text/plain");
            }

            if (result.IsInvalidHmac)
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status401Unauthorized,
                    "urn:rpg:problem:unauthorized",
                    "Unauthorized",
                    result.ErrorMessage ?? "Adyen HMAC verification failed.");
            }

            if (result.IsReplay)
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status409Conflict,
                    PaymentProblemTypes.Conflict,
                    "Conflict",
                    result.ErrorMessage ?? "Adyen notification replay detected.");
            }

            return ProblemResult(
                httpContext,
                StatusCodes.Status400BadRequest,
                PaymentProblemTypes.Validation,
                "Bad Request",
                result.ErrorMessage ?? "Adyen notification ingestion failed.");
        };

        app.MapPost("/webhooks/adyen", handler)
            .WithTags("Webhooks")
            .AllowAnonymous();

        app.MapPost("/api/v1/webhooks/adyen", handler)
            .WithTags("Webhooks")
            .AllowAnonymous();

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
