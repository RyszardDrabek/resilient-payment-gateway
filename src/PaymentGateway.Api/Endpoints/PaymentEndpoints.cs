using MediatR;
using PaymentGateway.Application.Payments;

namespace PaymentGateway.Api.Endpoints;

public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/payments")
            .WithTags("Payments")
            .RequireAuthorization();

        group.MapPost("/", async (AuthorizePaymentRequest request, ISender mediator, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.PartyId) ||
                request.Amount <= 0 ||
                string.IsNullOrWhiteSpace(request.Currency) ||
                request.Currency.Trim().Length != 3)
            {
                return Results.BadRequest(new
                {
                    error = "Invalid payment request parameters. Amount must be greater than zero, PartyId is required, and Currency must be a 3-letter ISO code."
                });
            }

            var command = new AuthorizePaymentCommand(
                request.PartyId,
                request.Amount,
                request.Currency,
                request.SettlementChannel);

            var result = await mediator.Send(command, ct);
            return Results.Created($"/payments/{result.PaymentId}", result);
        });

        group.MapGet("/{id}", async (string id, ISender mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetPaymentByIdQuery(id), ct);
            return result is not null ? Results.Ok(result) : Results.NotFound();
        });

        return app;
    }
}
