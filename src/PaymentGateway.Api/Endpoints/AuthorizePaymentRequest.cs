namespace PaymentGateway.Api.Endpoints;

public record AuthorizePaymentRequest(
    string PartyId,
    long Amount,
    string Currency,
    string? SettlementChannel = null,
    string? IdempotencyKey = null,
    string? TransactionOrigin = null,
    string? PaymentMethodId = null,
    string? SubscriptionId = null,
    bool StorePaymentMethod = false);
