using System.Net;
using System.Net.Mime;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PaymentGateway.Domain.Exceptions;
using PaymentGateway.Edge.Problems;

namespace PaymentGateway.Edge.Middleware;

/// <summary>
/// Global exception handler that maps payment-domain exceptions to RFC 7807 problem documents (ADR-007).
/// Sits at the outermost pipeline position so no domain exception reaches ASP.NET's default handler.
/// Never exposes stack traces or secrets in responses.
/// </summary>
public sealed class PaymentExceptionHandlerMiddleware(
    RequestDelegate next,
    ILogger<PaymentExceptionHandlerMiddleware> logger)
{
    private const string CorrelationIdHeader = "X-Correlation-Id";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (IdempotencyKeyMissingException ex)
        {
            await WriteProblemAsync(
                context,
                HttpStatusCode.BadRequest,
                PaymentProblemTypes.Validation,
                "Validation Failed",
                ex.Message);
        }
        catch (IdempotencyConflictException ex)
        {
            await WriteProblemAsync(
                context,
                HttpStatusCode.Conflict,
                PaymentProblemTypes.Conflict,
                "Conflict",
                ex.Message);
        }
        catch (IdempotencyInFlightException ex)
        {
            await WriteProblemAsync(
                context,
                HttpStatusCode.Conflict,
                PaymentProblemTypes.Conflict,
                "Conflict",
                ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Unhandled server error processing request. CorrelationId: {CorrelationId}",
                context.TraceIdentifier);

            // Never include ex.Message or stack trace — may contain secrets or instrument data.
            await WriteProblemAsync(
                context,
                HttpStatusCode.InternalServerError,
                PaymentProblemTypes.ServerError,
                "Server Error",
                "An unexpected error occurred.");
        }
    }

    private static async Task WriteProblemAsync(
        HttpContext context,
        HttpStatusCode statusCode,
        string type,
        string title,
        string detail)
    {
        var correlationId = context.TraceIdentifier;

        if (!context.Response.Headers.ContainsKey(CorrelationIdHeader))
        {
            context.Response.Headers[CorrelationIdHeader] = correlationId;
        }

        var problem = new PaymentProblemDetails(
            Type: type,
            Title: title,
            Status: (int)statusCode,
            Detail: detail,
            CorrelationId: correlationId);

        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/problem+json";

        var json = JsonSerializer.Serialize(problem, JsonOptions);
        await context.Response.WriteAsync(json, context.RequestAborted);
    }
}
