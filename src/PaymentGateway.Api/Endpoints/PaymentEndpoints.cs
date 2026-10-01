using System.Net.Mime;
using System.Text.Json;
using MediatR;
using PaymentGateway.Application.Payments;
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
            .RequireAuthorization();

        group.MapPost("/", async (HttpContext httpContext, AuthorizePaymentRequest request, ISender mediator, CancellationToken ct) =>
        {
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
                request.Currency.Trim().Length != 3)
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status400BadRequest,
                    PaymentProblemTypes.Validation,
                    "Validation Failed",
                    "Invalid payment request parameters. Amount must be greater than zero, PartyId is required, and Currency must be a 3-letter ISO code.");
            }

            var command = new AuthorizePaymentCommand(
                idempotencyKey,
                request.PartyId,
                request.Amount,
                request.Currency,
                request.SettlementChannel);

            // Domain exceptions (IdempotencyConflictException, IdempotencyInFlightException, etc.)
            // are handled by PaymentExceptionHandlerMiddleware — no try/catch needed here.
            var result = await mediator.Send(command, ct);
            return Results.Created($"/payments/{result.PaymentId}", result);
        });

        group.MapGet("/{id}", async (string id, HttpContext httpContext, ISender mediator, CancellationToken ct) =>
        {
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
