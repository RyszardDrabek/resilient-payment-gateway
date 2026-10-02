namespace PaymentGateway.Infrastructure.Events;

public interface ITestEventStore
{
    void Record(PaymentGateway.Application.Events.PaymentLifecycleEvent @event);
    IReadOnlyList<PaymentGateway.Application.Events.PaymentLifecycleEvent> GetEvents();
    void Clear();
}
