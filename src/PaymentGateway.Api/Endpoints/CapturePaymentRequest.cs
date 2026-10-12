namespace PaymentGateway.Api.Endpoints;

public record CapturePaymentRequest(long? Amount = null);
