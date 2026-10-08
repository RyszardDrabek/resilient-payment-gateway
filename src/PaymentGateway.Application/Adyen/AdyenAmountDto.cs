namespace PaymentGateway.Application.Adyen;

public sealed record AdyenAmountDto(
    string Currency,
    long Value);
