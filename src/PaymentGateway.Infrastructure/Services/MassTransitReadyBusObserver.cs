using MassTransit;

namespace PaymentGateway.Infrastructure.Services;

/// <summary>
/// Ensures the MassTransit bus startup sequence waits for all receive endpoints
/// to complete their initialization before the host considers the bus started.
/// Prevents shutdown race conditions (such as NRE in ReceiveEndpoint.Stop) when
/// short-lived test hosts or rapid shutdown sequences are invoked.
/// </summary>
public sealed class MassTransitReadyBusObserver : IBusObserver
{
    public void PostCreate(IBus bus) { }
    public void CreateFaulted(Exception exception) { }
    public Task PreStart(IBus bus) => Task.CompletedTask;

    public async Task PostStart(IBus bus, Task<BusReady> busReady)
    {
        await busReady.ConfigureAwait(false);
    }

    public Task StartFaulted(IBus bus, Exception exception) => Task.CompletedTask;
    public Task PreStop(IBus bus) => Task.CompletedTask;
    public Task PostStop(IBus bus) => Task.CompletedTask;
    public Task StopFaulted(IBus bus, Exception exception) => Task.CompletedTask;
}
