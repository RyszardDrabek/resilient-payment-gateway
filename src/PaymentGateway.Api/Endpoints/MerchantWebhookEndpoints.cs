using System.Security.Claims;
using System.Text.Json;
using MediatR;
using PaymentGateway.Application.Webhooks;
using PaymentGateway.Edge.Problems;
using PaymentGateway.Edge.RateLimiting;

namespace PaymentGateway.Api.Endpoints;

public static class MerchantWebhookEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IEndpointRouteBuilder MapMerchantWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/webhooks/merchants")
            .WithTags("Webhooks - Merchants")
            .RequireAuthorization("MerchantWebhookPolicy")
            .RequireRateLimiting(PaymentRateLimitingExtensions.PolicyName);

        group.MapGet("/subscription", async (
            HttpContext httpContext,
            ISender mediator,
            string? partyId,
            CancellationToken ct) =>
        {
            var resolvedPartyId = ResolvePartyId(httpContext, partyId);
            if (string.IsNullOrWhiteSpace(resolvedPartyId))
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status400BadRequest,
                    PaymentProblemTypes.Validation,
                    "Validation Failed",
                    "Unable to resolve merchant party identifier from token claims.");
            }

            var subscription = await mediator.Send(new GetMerchantWebhookSubscriptionQuery(resolvedPartyId), ct);
            if (subscription is null)
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status404NotFound,
                    PaymentProblemTypes.NotFound,
                    "Not Found",
                    $"No webhook subscription registered for party '{resolvedPartyId}'.");
            }

            return Results.Ok(subscription);
        });

        group.MapPut("/subscription", async (
            HttpContext httpContext,
            UpdateWebhookSubscriptionRequest request,
            ISender mediator,
            string? partyId,
            CancellationToken ct) =>
        {
            if (request is null || string.IsNullOrWhiteSpace(request.EndpointUrl))
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status400BadRequest,
                    PaymentProblemTypes.Validation,
                    "Validation Failed",
                    "EndpointUrl is required.");
            }

            if (!Uri.TryCreate(request.EndpointUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status400BadRequest,
                    PaymentProblemTypes.Validation,
                    "Validation Failed",
                    "EndpointUrl must be a valid absolute HTTP or HTTPS URL.");
            }

            var resolvedPartyId = ResolvePartyId(httpContext, partyId);
            if (string.IsNullOrWhiteSpace(resolvedPartyId))
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status400BadRequest,
                    PaymentProblemTypes.Validation,
                    "Validation Failed",
                    "Unable to resolve merchant party identifier from token claims.");
            }

            var command = new UpdateMerchantWebhookSubscriptionCommand(
                resolvedPartyId,
                request.EndpointUrl,
                request.IsActive);

            var result = await mediator.Send(command, ct);
            return Results.Ok(result);
        });

        group.MapPost("/subscription/rotate-secret", async (
            HttpContext httpContext,
            ISender mediator,
            string? partyId,
            CancellationToken ct) =>
        {
            var resolvedPartyId = ResolvePartyId(httpContext, partyId);
            if (string.IsNullOrWhiteSpace(resolvedPartyId))
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status400BadRequest,
                    PaymentProblemTypes.Validation,
                    "Validation Failed",
                    "Unable to resolve merchant party identifier from token claims.");
            }

            try
            {
                var result = await mediator.Send(new RotateMerchantWebhookSecretCommand(resolvedPartyId), ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status400BadRequest,
                    PaymentProblemTypes.Validation,
                    "Validation Failed",
                    ex.Message);
            }
        });

        group.MapPost("/test-dispatch", async (
            HttpContext httpContext,
            TestWebhookDispatchRequest? request,
            ISender mediator,
            string? partyId,
            CancellationToken ct) =>
        {
            var resolvedPartyId = ResolvePartyId(httpContext, partyId);
            if (string.IsNullOrWhiteSpace(resolvedPartyId))
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status400BadRequest,
                    PaymentProblemTypes.Validation,
                    "Validation Failed",
                    "Unable to resolve merchant party identifier from token claims.");
            }

            if (!string.IsNullOrWhiteSpace(request?.EndpointUrl) &&
                (!Uri.TryCreate(request.EndpointUrl, UriKind.Absolute, out var uri) ||
                 (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status400BadRequest,
                    PaymentProblemTypes.Validation,
                    "Validation Failed",
                    "Provided EndpointUrl must be a valid absolute HTTP or HTTPS URL.");
            }

            try
            {
                var command = new TestMerchantWebhookDispatchCommand(
                    PartyId: resolvedPartyId,
                    EndpointUrl: request?.EndpointUrl,
                    EventType: request?.EventType);

                var result = await mediator.Send(command, ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status400BadRequest,
                    PaymentProblemTypes.Validation,
                    "Validation Failed",
                    ex.Message);
            }
        });

        return app;
    }

    private static string? ResolvePartyId(HttpContext httpContext, string? queryPartyId)
    {
        var role = httpContext.User.FindFirst("role")?.Value
            ?? httpContext.User.FindFirst(ClaimTypes.Role)?.Value;

        if (string.Equals(role, "ops", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(queryPartyId))
        {
            return queryPartyId.Trim();
        }

        return httpContext.User.FindFirst("party_id")?.Value
            ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? httpContext.User.FindFirst("sub")?.Value;
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
