using MediatR;
using Microsoft.AspNetCore.Mvc;
using PaymentGateway.Application.Payouts;
using PaymentGateway.Edge.Problems;

namespace PaymentGateway.Api.Endpoints;

public record GeneratePayoutRequest(
    string MerchantId,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    string Currency);

public static class PayoutEndpoints
{
    public static IEndpointRouteBuilder MapPayoutEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/payouts")
            .WithTags("Payouts")
            .RequireAuthorization();

        group.MapGet("/", async (
            [FromQuery] string merchantId,
            [FromQuery] int? skip,
            [FromQuery] int? take,
            ISender mediator,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(merchantId))
            {
                return Results.BadRequest(new { error = "merchantId is required." });
            }

            var batches = await mediator.Send(new ListPayoutBatchesQuery(merchantId, skip ?? 0, take ?? 50), ct);
            return Results.Ok(batches);
        });

        group.MapGet("/{id}", async (
            string id,
            ISender mediator,
            CancellationToken ct) =>
        {
            var batch = await mediator.Send(new GetPayoutBatchQuery(id), ct);
            return batch is not null ? Results.Ok(batch) : Results.NotFound(new { error = $"Payout batch '{id}' not found." });
        });

        group.MapPost("/generate", async (
            HttpContext httpContext,
            GeneratePayoutRequest request,
            ISender mediator,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.MerchantId) ||
                string.IsNullOrWhiteSpace(request.Currency) ||
                request.PeriodStart >= request.PeriodEnd)
            {
                return Results.BadRequest(new { error = "Valid MerchantId, Currency, and PeriodStart < PeriodEnd are required." });
            }

            var result = await mediator.Send(new GeneratePayoutBatchCommand(
                request.MerchantId,
                request.PeriodStart,
                request.PeriodEnd,
                request.Currency), ct);

            return Results.Created($"/api/v1/payouts/{result.Id}", result);
        });

        group.MapGet("/{id}/csv", async (
            string id,
            ISender mediator,
            CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetPayoutBatchCsvQuery(id), ct);
            if (result is null)
            {
                return Results.NotFound(new { error = $"Payout batch '{id}' not found." });
            }

            return Results.File(
                System.Text.Encoding.UTF8.GetBytes(result.CsvContent),
                contentType: "text/csv; charset=utf-8",
                fileDownloadName: result.FileName);
        });

        return app;
    }
}
