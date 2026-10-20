using FluentAssertions;
using MassTransit;
using PaymentGateway.Infrastructure.Services;
using Xunit;

namespace PaymentGateway.UnitTests.Infrastructure.Services;

public sealed class MassTransitReadyBusObserverTests
{
    [Fact]
    public async Task PostStart_WhenBusReadyCompletes_CompletesSuccessfully()
    {
        var observer = new MassTransitReadyBusObserver();
        var tcs = new TaskCompletionSource<BusReady>();
        var readyTask = tcs.Task;

        var postStartTask = observer.PostStart(null!, readyTask);
        postStartTask.IsCompleted.Should().BeFalse();

        tcs.SetResult(null!);
        await postStartTask;

        postStartTask.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public void SynchronousEvents_DoNotThrow()
    {
        var observer = new MassTransitReadyBusObserver();

        var act1 = () => observer.PostCreate(null!);
        var act2 = () => observer.CreateFaulted(new Exception("test"));

        act1.Should().NotThrow();
        act2.Should().NotThrow();
    }
}
