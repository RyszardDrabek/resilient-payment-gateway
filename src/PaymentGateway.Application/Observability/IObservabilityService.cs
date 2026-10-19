namespace PaymentGateway.Application.Observability;

public interface IObservabilityService
{
    Task<ObservabilitySummaryDto> GetSummaryAsync(CancellationToken ct = default);
}
