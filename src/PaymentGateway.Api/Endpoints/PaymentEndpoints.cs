using MediatR;
using PaymentGateway.Application.Payments;

namespace PaymentGateway.Api.Endpoints;

public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/payments").WithTags("Payments");

        group.MapPost("/", async (AuthorizePaymentRequest request, ISender mediator, CancellationToken ct) =>
        {
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
