namespace PaymentGateway.Api.Endpoints;

public record AuthorizePaymentRequest(
    string PartyId,
    long Amount,
    string Currency,
    string? SettlementChannel = null);
