using System.Collections.Concurrent;
using PaymentGateway.Application.Events;

namespace PaymentGateway.Infrastructure.Events;

public sealed class InMemoryTestEventStore : ITestEventStore
{
    private readonly ConcurrentBag<PaymentLifecycleEvent> _events = [];

    public void Record(PaymentLifecycleEvent @event)
    {
        _events.Add(@event);
    }

    public IReadOnlyList<PaymentLifecycleEvent> GetEvents()
    {
        return _events.ToList();
    }

    public void Clear()
    {
        _events.Clear();
    }
}
