using System.Security.Claims;
using System.Text.Json;
using MediatR;
using PaymentGateway.Application.Webhooks;
using PaymentGateway.Edge.Problems;
using PaymentGateway.Edge.RateLimiting;

namespace PaymentGateway.Api.Endpoints;

public static class MerchantWebhookDlqEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IEndpointRouteBuilder MapMerchantWebhookDlqEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/merchants/{merchantId}/webhooks/dlq")
            .WithTags("Merchant Webhook DLQ")
            .RequireAuthorization()
            .RequireRateLimiting(PaymentRateLimitingExtensions.PolicyName);

        group.MapGet("/", async (
            string merchantId,
            string? status,
            int? page,
            int? pageSize,
            HttpContext httpContext,
            ISender mediator,
            CancellationToken ct) =>
        {
            if (!IsAuthorizedForMerchant(httpContext.User, merchantId))
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status403Forbidden,
                    PaymentProblemTypes.Forbidden,
                    "Forbidden",
                    $"Caller is not authorized to access webhook DLQ for merchant '{merchantId}'.");
            }

            var query = new ListMerchantWebhookDlqQuery(
                MerchantId: merchantId,
                Status: status,
                Page: page ?? 1,
                PageSize: pageSize ?? 50);

            var items = await mediator.Send(query, ct);
            return Results.Ok(items);
        });

        group.MapPost("/{dlqEntryId:guid}/replay", async (
            string merchantId,
            Guid dlqEntryId,
            HttpContext httpContext,
            ISender mediator,
            CancellationToken ct) =>
        {
            if (!IsAuthorizedForMerchant(httpContext.User, merchantId))
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status403Forbidden,
                    PaymentProblemTypes.Forbidden,
                    "Forbidden",
                    $"Caller is not authorized to replay webhook DLQ entries for merchant '{merchantId}'.");
            }

            var result = await mediator.Send(new ReplayMerchantWebhookCommand(merchantId, dlqEntryId), ct);
            if (result is null)
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status404NotFound,
                    PaymentProblemTypes.NotFound,
                    "Not Found",
                    $"Webhook DLQ entry '{dlqEntryId}' was not found for merchant '{merchantId}'.");
            }

            return Results.Ok(result);
        });

        return app;
    }

    private static bool IsAuthorizedForMerchant(ClaimsPrincipal user, string targetMerchantId)
    {
        var role = user.FindFirst("role")?.Value
            ?? user.FindFirst(ClaimTypes.Role)?.Value;

        if (string.Equals(role, "ops", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var merchantId = user.FindFirst("merchant_id")?.Value
            ?? user.FindFirst("merchantId")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value;

        return string.Equals(merchantId, targetMerchantId, StringComparison.OrdinalIgnoreCase);
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
