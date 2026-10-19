using PaymentGateway.Domain.Entities;

namespace PaymentGateway.Domain.Ports;

public interface IFeeScheduleRepository
{
    Task<FeeSchedule?> GetActiveFeeScheduleAsync(string merchantId, string currency, DateTimeOffset asOf, CancellationToken ct = default);
    Task SaveAsync(FeeSchedule schedule, CancellationToken ct = default);
}
