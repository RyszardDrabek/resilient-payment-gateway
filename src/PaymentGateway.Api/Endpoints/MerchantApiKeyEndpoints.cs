using System.Security.Claims;
using System.Text.Json;
using MediatR;
using PaymentGateway.Application.ApiKeys;
using PaymentGateway.Edge.Problems;
using PaymentGateway.Edge.RateLimiting;

namespace PaymentGateway.Api.Endpoints;

public sealed record CreateApiKeyRequest(string Name, IReadOnlyList<string>? Scopes, bool? IsLive);

public static class MerchantApiKeyEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IEndpointRouteBuilder MapMerchantApiKeyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/merchants/{merchantId}/api-keys")
            .WithTags("Merchant API Keys")
            .RequireAuthorization()
            .RequireRateLimiting(PaymentRateLimitingExtensions.PolicyName);

        group.MapGet("/", async (
            string merchantId,
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
                    $"Caller is not authorized to access API keys for merchant '{merchantId}'.");
            }

            var keys = await mediator.Send(new ListMerchantApiKeysQuery(merchantId), ct);
            return Results.Ok(keys);
        });

        group.MapPost("/", async (
            string merchantId,
            CreateApiKeyRequest request,
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
                    $"Caller is not authorized to create API keys for merchant '{merchantId}'.");
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status400BadRequest,
                    PaymentProblemTypes.Validation,
                    "Validation Failed",
                    "API key 'name' is required.");
            }

            var scopes = request.Scopes is not null && request.Scopes.Count > 0
                ? request.Scopes
                : ["payments:read"];

            var command = new CreateMerchantApiKeyCommand(
                MerchantId: merchantId,
                Name: request.Name,
                Scopes: scopes,
                IsLive: request.IsLive ?? true);

            var created = await mediator.Send(command, ct);

            return Results.Created($"/api/v1/merchants/{merchantId}/api-keys/{created.Id}", created);
        });

        group.MapPost("/{keyId:guid}/revoke", async (
            string merchantId,
            Guid keyId,
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
                    $"Caller is not authorized to revoke API keys for merchant '{merchantId}'.");
            }

            var revoked = await mediator.Send(new RevokeMerchantApiKeyCommand(merchantId, keyId), ct);
            if (!revoked)
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status404NotFound,
                    PaymentProblemTypes.NotFound,
                    "Not Found",
                    $"API key '{keyId}' was not found for merchant '{merchantId}'.");
            }

            return Results.Ok(new { message = "API key revoked successfully." });
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
