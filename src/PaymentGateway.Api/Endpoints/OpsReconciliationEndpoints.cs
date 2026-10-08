using PaymentGateway.Application.Adyen;

namespace PaymentGateway.Api.Endpoints;

public static class OpsReconciliationEndpoints
{
    public static IEndpointRouteBuilder MapOpsReconciliationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/ops/reconciliation");

        group.MapGet("/uncorrelated", async (IAdyenReconciliationService reconciliationService, CancellationToken ct) =>
        {
            var notifications = await reconciliationService.GetUncorrelatedNotificationsAsync(ct);
            var response = notifications.Select(n => new
            {
                n.Id,
                n.PspReference,
                n.MerchantReference,
                n.OriginalReference,
                n.EventCode,
                n.AmountValue,
                n.AmountCurrency,
                n.Success,
                n.Status,
                n.CreatedAt
            });

            return Results.Ok(response);
        });

        group.MapGet("/unresolved", async (IAdyenReconciliationService reconciliationService, CancellationToken ct) =>
        {
            var payments = await reconciliationService.GetUnresolvedPaymentsAsync(ct);
            var response = payments.Select(p => new
            {
                p.Id,
                p.PartyId,
                p.Amount,
                p.Currency,
                State = p.State.ToString(),
                p.SettlementChannel,
                p.ChannelReference,
                p.CreatedAt
            });

            return Results.Ok(response);
        });

        group.MapPost("/payments/{paymentId}/reconcile", async (string paymentId, IAdyenReconciliationService reconciliationService, CancellationToken ct) =>
        {
            var result = await reconciliationService.ReconcileUnresolvedPaymentAsync(paymentId, null, ct);
            return Results.Ok(result);
        });

        group.MapPost("/notifications/{notificationId:guid}/apply", async (Guid notificationId, IAdyenReconciliationService reconciliationService, CancellationToken ct) =>
        {
            var result = await reconciliationService.ReconcileNotificationAsync(notificationId, ct);
            return Results.Ok(result);
        });

        group.MapPost("/reconcile-all", async (IAdyenReconciliationService reconciliationService, CancellationToken ct) =>
        {
            var results = await reconciliationService.ReconcileAllPendingAsync(ct);
            return Results.Ok(results);
        });

        return app;
    }
}
