using System.Net.Mime;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using PaymentGateway.Application.PaymentMethods;
using PaymentGateway.Edge.Problems;
using PaymentGateway.Edge.RateLimiting;

namespace PaymentGateway.Api.Endpoints;

public record TokenizePaymentMethodRequest(
    string PartyId,
    string SettlementChannel,
    string CardBrand,
    string MaskedPan,
    int ExpiryMonth,
    int ExpiryYear,
    string Token);

public static class PaymentMethodEndpoints
{
    public static IEndpointRouteBuilder MapPaymentMethodEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/payment-methods")
            .WithTags("Payment Methods")
            .RequireAuthorization()
            .RequireRateLimiting(PaymentRateLimitingExtensions.PolicyName);

        group.MapGet("/", async (
            HttpContext httpContext,
            ISender mediator,
            [FromQuery] string? partyId,
            [FromQuery] bool activeOnly = true,
            CancellationToken ct = default) =>
        {
            var resolvedPartyId = !string.IsNullOrWhiteSpace(partyId)
                ? partyId
                : httpContext.User.FindFirst("merchant_id")?.Value
                  ?? httpContext.User.FindFirst("party_id")?.Value
                  ?? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrWhiteSpace(resolvedPartyId))
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status400BadRequest,
                    PaymentProblemTypes.Validation,
                    "Validation Failed",
                    "PartyId query parameter is required.");
            }

            var methods = await mediator.Send(new GetPaymentMethodsQuery(resolvedPartyId, activeOnly), ct);
            return Results.Ok(methods);
        });

        group.MapPost("/", async (
            HttpContext httpContext,
            TokenizePaymentMethodRequest request,
            ISender mediator,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.PartyId) ||
                string.IsNullOrWhiteSpace(request.SettlementChannel) ||
                string.IsNullOrWhiteSpace(request.CardBrand) ||
                string.IsNullOrWhiteSpace(request.MaskedPan) ||
                string.IsNullOrWhiteSpace(request.Token))
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status400BadRequest,
                    PaymentProblemTypes.Validation,
                    "Validation Failed",
                    "PartyId, SettlementChannel, CardBrand, MaskedPan, and Token are required.");
            }

            var command = new TokenizePaymentMethodCommand(
                request.PartyId,
                request.SettlementChannel,
                request.CardBrand,
                request.MaskedPan,
                request.ExpiryMonth,
                request.ExpiryYear,
                request.Token);

            var result = await mediator.Send(command, ct);
            return Results.Created($"/api/v1/payment-methods/{result.Id}", result);
        });

        group.MapDelete("/{id}", async (
            string id,
            ISender mediator,
            CancellationToken ct) =>
        {
            var revoked = await mediator.Send(new RevokePaymentMethodCommand(id), ct);
            return revoked ? Results.NoContent() : Results.NotFound();
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

        return Results.Json(
            problem,
            contentType: MediaTypeNames.Application.ProblemJson,
            statusCode: statusCode);
    }
}
