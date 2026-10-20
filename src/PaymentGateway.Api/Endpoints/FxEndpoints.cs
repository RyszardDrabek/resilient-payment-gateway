using System.Text.Json;
using MediatR;
using PaymentGateway.Application.Fx.Commands;
using PaymentGateway.Application.Fx.Ports;
using PaymentGateway.Application.Fx.Queries;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Edge.Problems;
using PaymentGateway.Edge.RateLimiting;

namespace PaymentGateway.Api.Endpoints;

public sealed record CreateFxQuoteRequest(
    string BaseCurrency,
    long BaseAmount,
    string TargetCurrency,
    string? PartyId = null,
    int? TtlMinutes = null);

public static class FxEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IEndpointRouteBuilder MapFxEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/fx")
            .WithTags("Foreign Exchange")
            .RequireAuthorization()
            .RequireRateLimiting(PaymentRateLimitingExtensions.PolicyName);

        group.MapPost("/quotes", async (
            CreateFxQuoteRequest request,
            HttpContext httpContext,
            ISender mediator,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.BaseCurrency) ||
                string.IsNullOrWhiteSpace(request.TargetCurrency) ||
                request.BaseAmount <= 0)
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status400BadRequest,
                    PaymentProblemTypes.Validation,
                    "Validation Failed",
                    "Invalid quote parameters: baseCurrency, targetCurrency, and a positive baseAmount are required.");
            }

            var trimmedBase = request.BaseCurrency.Trim();
            var trimmedTarget = request.TargetCurrency.Trim();

            if ((trimmedBase.Length != 3 && !Payment.IsSupportedCryptoAsset(trimmedBase)) ||
                (trimmedTarget.Length != 3 && !Payment.IsSupportedCryptoAsset(trimmedTarget)))
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status400BadRequest,
                    PaymentProblemTypes.Validation,
                    "Validation Failed",
                    "Currencies must be 3-letter ISO codes or supported crypto assets (e.g. USDC, USDT).");
            }

            try
            {
                var command = new CreateFxQuoteCommand(
                    trimmedBase,
                    request.BaseAmount,
                    trimmedTarget,
                    request.PartyId,
                    request.TtlMinutes);

                var quote = await mediator.Send(command, ct);
                return Results.Created($"/fx/quotes/{quote.QuoteId}", quote);
            }
            catch (NotSupportedException ex)
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status400BadRequest,
                    PaymentProblemTypes.Validation,
                    "Unsupported Currency",
                    ex.Message);
            }
        });

        group.MapGet("/quotes/{quoteId}", async (
            string quoteId,
            HttpContext httpContext,
            ISender mediator,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(quoteId))
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status400BadRequest,
                    PaymentProblemTypes.Validation,
                    "Validation Failed",
                    "Quote ID is required.");
            }

            var quote = await mediator.Send(new GetFxQuoteByIdQuery(quoteId), ct);
            if (quote is null)
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status404NotFound,
                    PaymentProblemTypes.NotFound,
                    "Not Found",
                    $"Quote '{quoteId}' was not found.");
            }

            return Results.Ok(quote);
        });

        group.MapGet("/rates", async (
            string? baseCurrency,
            HttpContext httpContext,
            IFxRateProvider rateProvider,
            CancellationToken ct) =>
        {
            var currency = string.IsNullOrWhiteSpace(baseCurrency) ? "EUR" : baseCurrency.Trim().ToUpperInvariant();
            try
            {
                var rates = await rateProvider.GetRatesAsync(currency, ct);
                return Results.Ok(new { BaseCurrency = currency, Rates = rates });
            }
            catch (NotSupportedException ex)
            {
                return ProblemResult(
                    httpContext,
                    StatusCodes.Status400BadRequest,
                    PaymentProblemTypes.Validation,
                    "Unsupported Currency",
                    ex.Message);
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

        return Results.Json(problem, JsonOptions, "application/problem+json", statusCode);
    }
}
