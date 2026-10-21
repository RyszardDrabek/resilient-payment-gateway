using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PaymentGateway.Infrastructure.Locking;
using Xunit;

namespace PaymentGateway.UnitTests.Infrastructure.Locking;

public sealed class PostgresAdvisoryLockProviderTests
{
    [Fact]
    public void ComputeAdvisoryLockKey_ReturnsDeterministicKeys()
    {
        var key1 = PostgresAdvisoryLockProvider.ComputeAdvisoryLockKey("SubscriptionBillingWorker");
        var key2 = PostgresAdvisoryLockProvider.ComputeAdvisoryLockKey("SubscriptionBillingWorker");
        var keyOther = PostgresAdvisoryLockProvider.ComputeAdvisoryLockKey("DailyReconciliationWorker");

        key1.Should().Be(key2);
        key1.Should().NotBe(keyOther);
    }

    [Fact]
    public async Task TryAcquireLockAsync_InLocalFallback_AcquiresExclusivelyAndReleasesOnDispose()
    {
        var config = new ConfigurationBuilder().Build();
        var provider = new PostgresAdvisoryLockProvider(config, NullLogger<PostgresAdvisoryLockProvider>.Instance);

        var lock1 = await provider.TryAcquireLockAsync("ResourceA");
        lock1.Should().NotBeNull();
        lock1!.ResourceName.Should().Be("ResourceA");

        // Concurrent acquisition on same resource should fail
        var lock2 = await provider.TryAcquireLockAsync("ResourceA");
        lock2.Should().BeNull();

        // Acquisition on different resource should succeed
        var lockB = await provider.TryAcquireLockAsync("ResourceB");
        lockB.Should().NotBeNull();

        // Release lock1
        await lock1.DisposeAsync();

        // Now ResourceA can be acquired again
        var lock1Reacquired = await provider.TryAcquireLockAsync("ResourceA");
        lock1Reacquired.Should().NotBeNull();

        await lockB!.DisposeAsync();
        await lock1Reacquired!.DisposeAsync();
    }
}
