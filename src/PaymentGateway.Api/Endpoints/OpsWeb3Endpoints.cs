using PaymentGateway.Application.Web3;

namespace PaymentGateway.Api.Endpoints;

public static class OpsWeb3Endpoints
{
    public static IEndpointRouteBuilder MapOpsWeb3Endpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/ops/web3");

        group.MapGet("/pending", async (IWeb3FinalityWatcherService watcherService, CancellationToken ct) =>
        {
            var pending = await watcherService.GetPendingSettlementsAsync(ct);
            var response = pending.Select(p => new
            {
                p.PaymentId,
                p.TransactionHash,
                p.ConfirmationDepth,
                p.RequiredConfirmations,
                Status = p.Status.ToStatusString(),
                p.Message
            });

            return Results.Ok(response);
        });

        group.MapPost("/payments/{paymentId}/confirm", async (string paymentId, IWeb3FinalityWatcherService watcherService, CancellationToken ct) =>
        {
            var result = await watcherService.ObserveAndApplyFinalityAsync(paymentId, ct);
            return Results.Ok(new
            {
                result.PaymentId,
                result.TransactionHash,
                result.ConfirmationDepth,
                result.RequiredConfirmations,
                Status = result.Status.ToStatusString(),
                result.Message
            });
        });

        group.MapPost("/watch-all", async (IWeb3FinalityWatcherService watcherService, CancellationToken ct) =>
        {
            var results = await watcherService.WatchAllPendingSettlementsAsync(ct);
            var response = results.Select(r => new
            {
                r.PaymentId,
                r.TransactionHash,
                r.ConfirmationDepth,
                r.RequiredConfirmations,
                Status = r.Status.ToStatusString(),
                r.Message
            });

            return Results.Ok(response);
        });

        return app;
    }
}
