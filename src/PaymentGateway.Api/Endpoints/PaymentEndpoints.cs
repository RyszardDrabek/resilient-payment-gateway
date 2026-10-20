using System.Net.Mime;
using System.Text.Json;
using MediatR;
using PaymentGateway.Application.Payments;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Edge.Problems;

namespace PaymentGateway.Api.Endpoints;

public static class PaymentEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/payments")
            .WithTags("Payments")
            .RequireAuthorization()
            .RequireRateLimiting(PaymentGateway.Edge.RateLimiting.PaymentRateLimitingExtensions.PolicyName);

        group.MapPost("/", async (HttpContext httpContext, AuthorizePaymentRequest request, ISender mediator, CancellationToken ct) =>
        {
            if (!HasRequiredScope(httpContext, "payments:write"))
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status403Forbidden,
                    PaymentProblemTypes.Forbidden,
                    "Forbidden",
                    "The API key lacks the required 'payments:write' scope.");
            }

            var idempotencyKey = httpContext.Request.Headers["Idempotency-Key"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                idempotencyKey = request.IdempotencyKey;
            }

            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status400BadRequest,
                    PaymentProblemTypes.Validation,
                    "Validation Failed",
                    "Idempotency-Key is required on payment commands.");
            }

            if (string.IsNullOrWhiteSpace(request.PartyId) ||
                request.Amount <= 0 ||
                string.IsNullOrWhiteSpace(request.Currency) ||
                (request.Currency.Trim().Length != 3 && !Payment.IsSupportedCryptoAsset(request.Currency.Trim())))
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status400BadRequest,
                    PaymentProblemTypes.Validation,
                    "Validation Failed",
                    "Invalid payment request parameters. Amount must be greater than zero, PartyId is required, and Currency must be a 3-letter ISO code or supported crypto asset.");
            }

            if (request.QuoteId is not null &&
                (string.IsNullOrWhiteSpace(request.QuoteId) || !request.QuoteId.StartsWith("fxq_", StringComparison.OrdinalIgnoreCase)))
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status400BadRequest,
                    PaymentProblemTypes.Validation,
                    "Validation Failed",
                    "Invalid QuoteId format. When provided, QuoteId must start with 'fxq_'.");
            }

            var command = new AuthorizePaymentCommand(
                idempotencyKey,
                request.PartyId,
                request.Amount,
                request.Currency,
                request.SettlementChannel,
                request.TransactionOrigin,
                request.PaymentMethodId,
                request.SubscriptionId,
                request.StorePaymentMethod,
                request.QuoteId);

            // Domain exceptions (IdempotencyConflictException, IdempotencyInFlightException, etc.)
            // are handled by PaymentExceptionHandlerMiddleware — no try/catch needed here.
            var result = await mediator.Send(command, ct);
            if (!httpContext.Response.Headers.ContainsKey("X-Correlation-Id"))
            {
                httpContext.Response.Headers["X-Correlation-Id"] = httpContext.TraceIdentifier;
            }
            return Results.Created($"/payments/{result.PaymentId}", result);
        });

        group.MapGet("/{id}", async (string id, HttpContext httpContext, ISender mediator, CancellationToken ct) =>
        {
            if (!HasRequiredScope(httpContext, "payments:read"))
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status403Forbidden,
                    PaymentProblemTypes.Forbidden,
                    "Forbidden",
                    "The API key lacks the required 'payments:read' scope.");
            }

            var result = await mediator.Send(new GetPaymentByIdQuery(id), ct);
            if (result is null)
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status404NotFound,
                    PaymentProblemTypes.NotFound,
                    "Not Found",
                    $"Payment '{id}' was not found.");
            }

            return Results.Ok(result);
        });

        group.MapPost("/{id}/capture", async (string id, HttpContext httpContext, ISender mediator, CancellationToken ct) =>
        {
            if (!HasRequiredScope(httpContext, "payments:write"))
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status403Forbidden,
                    PaymentProblemTypes.Forbidden,
                    "Forbidden",
                    "The API key lacks the required 'payments:write' scope.");
            }

            var idempotencyKey = httpContext.Request.Headers["Idempotency-Key"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status400BadRequest,
                    PaymentProblemTypes.Validation,
                    "Validation Failed",
                    "Idempotency-Key is required on payment commands.");
            }

            long? amount = null;
            if (httpContext.Request.ContentLength > 0)
            {
                try
                {
                    var body = await httpContext.Request.ReadFromJsonAsync<CapturePaymentRequest>(cancellationToken: ct);
                    amount = body?.Amount;
                }
                catch (System.Text.Json.JsonException)
                {
                    return ProblemResult(
                        httpContext,
                        StatusCodes.Status400BadRequest,
                        PaymentProblemTypes.Validation,
                        "Validation Failed",
                        "Invalid JSON payload.");
                }
            }

            if (amount.HasValue && amount.Value <= 0)
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status400BadRequest,
                    PaymentProblemTypes.Validation,
                    "Validation Failed",
                    "Capture amount must be greater than zero.");
            }

            var result = await mediator.Send(new CapturePaymentCommand(id, idempotencyKey, amount), ct);
            return Results.Ok(result);
        });

        group.MapPost("/{id}/cancel", async (string id, HttpContext httpContext, ISender mediator, CancellationToken ct) =>
        {
            if (!HasRequiredScope(httpContext, "payments:write"))
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status403Forbidden,
                    PaymentProblemTypes.Forbidden,
                    "Forbidden",
                    "The API key lacks the required 'payments:write' scope.");
            }

            var idempotencyKey = httpContext.Request.Headers["Idempotency-Key"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status400BadRequest,
                    PaymentProblemTypes.Validation,
                    "Validation Failed",
                    "Idempotency-Key is required on payment commands.");
            }

            var result = await mediator.Send(new CancelPaymentCommand(id, idempotencyKey), ct);
            return Results.Ok(result);
        });

        group.MapPost("/{id}/refund", async (string id, HttpContext httpContext, ISender mediator, CancellationToken ct) =>
        {
            if (!HasRequiredScope(httpContext, "payments:write"))
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status403Forbidden,
                    PaymentProblemTypes.Forbidden,
                    "Forbidden",
                    "The API key lacks the required 'payments:write' scope.");
            }

            var idempotencyKey = httpContext.Request.Headers["Idempotency-Key"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status400BadRequest,
                    PaymentProblemTypes.Validation,
                    "Validation Failed",
                    "Idempotency-Key is required on payment commands.");
            }

            long? amount = null;
            if (httpContext.Request.ContentLength > 0)
            {
                try
                {
                    var request = await httpContext.Request.ReadFromJsonAsync<RefundPaymentRequest>(cancellationToken: ct);
                    amount = request?.Amount;
                }
                catch (JsonException)
                {
                    return ProblemResult(
                        httpContext,
                        StatusCodes.Status400BadRequest,
                        PaymentProblemTypes.Validation,
                        "Validation Failed",
                        "Invalid JSON payload.");
                }
            }

            if (amount.HasValue && amount.Value <= 0)
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status400BadRequest,
                    PaymentProblemTypes.Validation,
                    "Validation Failed",
                    "Refund amount must be greater than zero.");
            }

            var result = await mediator.Send(new RefundPaymentCommand(id, idempotencyKey, amount), ct);
            return Results.Ok(result);
        });

        group.MapPost("/{id}/3ds-result", async (string id, HttpContext httpContext, ISender mediator, CancellationToken ct) =>
        {
            if (!HasRequiredScope(httpContext, "payments:write"))
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status403Forbidden,
                    PaymentProblemTypes.Forbidden,
                    "Forbidden",
                    "The API key lacks the required 'payments:write' scope.");
            }

            SubmitThreeDsResultRequest? request = null;
            if (httpContext.Request.ContentLength > 0)
            {
                try
                {
                    request = await httpContext.Request.ReadFromJsonAsync<SubmitThreeDsResultRequest>(cancellationToken: ct);
                }
                catch (JsonException)
                {
                    return ProblemResult(
                        httpContext,
                        StatusCodes.Status400BadRequest,
                        PaymentProblemTypes.Validation,
                        "Validation Failed",
                        "Invalid JSON payload.");
                }
            }

            var command = new SubmitThreeDsResultCommand(id, request?.ThreeDsResult, request?.Details);
            var result = await mediator.Send(command, ct);

            if (!httpContext.Response.Headers.ContainsKey("X-Correlation-Id"))
            {
                httpContext.Response.Headers["X-Correlation-Id"] = httpContext.TraceIdentifier;
            }

            return Results.Ok(result);
        });

        return app;
    }

    private static bool HasRequiredScope(HttpContext httpContext, string requiredScope)
    {
        var authMethod = httpContext.User.FindFirst("auth_method")?.Value;
        if (string.Equals(authMethod, "api_key", StringComparison.OrdinalIgnoreCase))
        {
            return httpContext.User.HasClaim(c => c.Type == "scope" && string.Equals(c.Value, requiredScope, StringComparison.OrdinalIgnoreCase));
        }

        return true;
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
