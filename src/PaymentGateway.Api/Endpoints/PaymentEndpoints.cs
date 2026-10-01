using MediatR;
using PaymentGateway.Application.Payments;
using PaymentGateway.Domain.Exceptions;

namespace PaymentGateway.Api.Endpoints;

public static class PaymentEndpoints
{
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
                return Results.BadRequest(new
                {
                    type = "urn:rpg:problem:validation",
                    title = "Validation Failed",
                    status = 400,
                    detail = "Idempotency-Key is required on payment commands."
                });
            }

            if (string.IsNullOrWhiteSpace(request.PartyId) ||
                request.Amount <= 0 ||
                string.IsNullOrWhiteSpace(request.Currency) ||
                request.Currency.Trim().Length != 3)
            {
                return Results.BadRequest(new
                {
                    type = "urn:rpg:problem:validation",
                    title = "Validation Failed",
                    status = 400,
                    detail = "Invalid payment request parameters. Amount must be greater than zero, PartyId is required, and Currency must be a 3-letter ISO code."
                });
            }

            try
            {
                var command = new AuthorizePaymentCommand(
                    idempotencyKey,
                    request.PartyId,
                    request.Amount,
                    request.Currency,
                    request.SettlementChannel);

                var result = await mediator.Send(command, ct);
                return Results.Created($"/payments/{result.PaymentId}", result);
            }
            catch (IdempotencyConflictException ex)
            {
                return Results.Conflict(new
                {
                    type = "urn:rpg:problem:conflict",
                    title = "Conflict",
                    status = 409,
                    detail = ex.Message
                });
            }
            catch (IdempotencyInFlightException ex)
            {
                return Results.Conflict(new
                {
                    type = "urn:rpg:problem:conflict",
                    title = "Conflict",
                    status = 409,
                    detail = ex.Message
                });
            }
        });

        group.MapGet("/{id}", async (string id, ISender mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetPaymentByIdQuery(id), ct);
            return result is not null ? Results.Ok(result) : Results.NotFound();
        });

        return app;
    }
}
