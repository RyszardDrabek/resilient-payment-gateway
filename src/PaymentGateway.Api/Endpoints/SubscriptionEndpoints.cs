using System.Net.Mime;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using PaymentGateway.Application.Subscriptions;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Edge.Problems;
using PaymentGateway.Edge.RateLimiting;

namespace PaymentGateway.Api.Endpoints;

public record CreateSubscriptionRequest(
    string PartyId,
    string PaymentMethodId,
    long Amount,
    string Currency,
    string Interval,
    DateTimeOffset? StartDate = null);

public static class SubscriptionEndpoints
{
    public static IEndpointRouteBuilder MapSubscriptionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/subscriptions")
            .WithTags("Subscriptions")
            .RequireAuthorization()
            .RequireRateLimiting(PaymentRateLimitingExtensions.PolicyName);

        group.MapPost("/", async (
            HttpContext httpContext,
            CreateSubscriptionRequest request,
            ISender mediator,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.PartyId) ||
                string.IsNullOrWhiteSpace(request.PaymentMethodId) ||
                request.Amount <= 0 ||
                string.IsNullOrWhiteSpace(request.Currency) ||
                !Enum.TryParse<SubscriptionInterval>(request.Interval, true, out var interval))
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status400BadRequest,
                    PaymentProblemTypes.Validation,
                    "Validation Failed",
                    "Valid PartyId, PaymentMethodId, Amount, Currency, and Interval (Daily, Weekly, Monthly, Yearly) are required.");
            }

            try
            {
                var command = new CreateSubscriptionCommand(
                    request.PartyId,
                    request.PaymentMethodId,
                    request.Amount,
                    request.Currency,
                    interval,
                    request.StartDate);

                var result = await mediator.Send(command, ct);
                return Results.Created($"/api/v1/subscriptions/{result.Id}", result);
            }
            catch (InvalidOperationException ex)
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status400BadRequest,
                    PaymentProblemTypes.Validation,
                    "Subscription Creation Failed",
                    ex.Message);
            }
        });

        group.MapGet("/", async (
            [FromQuery] string? partyId,
            [FromQuery] int skip = 0,
            [FromQuery] int take = 50,
            ISender mediator = default!,
            CancellationToken ct = default) =>
        {
            var result = await mediator.Send(new ListSubscriptionsQuery(partyId, skip, take), ct);
            return Results.Ok(result);
        });

        group.MapGet("/{id}", async (
            string id,
            ISender mediator,
            CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetSubscriptionByIdQuery(id), ct);
            return result is not null ? Results.Ok(result) : Results.NotFound();
        });

        group.MapPost("/{id}/pause", async (
            HttpContext httpContext,
            string id,
            ISender mediator,
            CancellationToken ct) =>
        {
            try
            {
                var result = await mediator.Send(new PauseSubscriptionCommand(id), ct);
                return Results.Ok(result);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status409Conflict,
                    PaymentProblemTypes.Conflict,
                    "Pause Failed",
                    ex.Message);
            }
        });

        group.MapPost("/{id}/resume", async (
            HttpContext httpContext,
            string id,
            ISender mediator,
            CancellationToken ct) =>
        {
            try
            {
                var result = await mediator.Send(new ResumeSubscriptionCommand(id), ct);
                return Results.Ok(result);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status409Conflict,
                    PaymentProblemTypes.Conflict,
                    "Resume Failed",
                    ex.Message);
            }
        });

        group.MapPost("/{id}/cancel", async (
            string id,
            ISender mediator,
            CancellationToken ct) =>
        {
            try
            {
                var result = await mediator.Send(new CancelSubscriptionCommand(id), ct);
                return Results.Ok(result);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
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
