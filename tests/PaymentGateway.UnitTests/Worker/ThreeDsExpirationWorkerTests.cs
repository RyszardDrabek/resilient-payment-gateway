using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Ports;
using PaymentGateway.Worker;
using Xunit;

namespace PaymentGateway.UnitTests.Worker;

public sealed class ThreeDsExpirationWorkerTests
{
    [Fact]
    public async Task ProcessExpirationCycleAsync_WhenExpiredPaymentsExist_ExpiresAndUpdatesEachPayment()
    {
        var expiredAction = new ThreeDsActionDetails("threeDS2", "https://bank.example.com", "token_exp", DateTimeOffset.UtcNow.AddMinutes(-5));
        var payment1 = Payment.CreateRequiresAction("merch_1", 1000, "EUR", "mock-channel", "ref_1", expiredAction);
        var payment2 = Payment.CreateRequiresAction("merch_2", 2000, "EUR", "mock-channel", "ref_2", expiredAction);

        var fakeRepo = new FakePaymentRepository([payment1, payment2]);
        var services = new ServiceCollection();
        services.AddScoped<IPaymentRepository>(_ => fakeRepo);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var worker = new ThreeDsExpirationWorker(scopeFactory, NullLogger<ThreeDsExpirationWorker>.Instance);

        var result = await worker.ProcessExpirationCycleAsync(CancellationToken.None);

        result.Should().Be(2);
        fakeRepo.UpdatedPayments.Should().HaveCount(2);

        payment1.State.Should().Be(PaymentState.Declined);
        payment1.DeclineReason.Should().Be("ThreeDsExpired");
        payment2.State.Should().Be(PaymentState.Declined);
        payment2.DeclineReason.Should().Be("ThreeDsExpired");
    }

    [Fact]
    public async Task ProcessExpirationCycleAsync_WhenNoExpiredPayments_ReturnsZero()
    {
        var fakeRepo = new FakePaymentRepository([]);
        var services = new ServiceCollection();
        services.AddScoped<IPaymentRepository>(_ => fakeRepo);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var worker = new ThreeDsExpirationWorker(scopeFactory, NullLogger<ThreeDsExpirationWorker>.Instance);

        var result = await worker.ProcessExpirationCycleAsync(CancellationToken.None);

        result.Should().Be(0);
        fakeRepo.UpdatedPayments.Should().BeEmpty();
    }

    private sealed class FakePaymentRepository(List<Payment> expiredPayments) : IPaymentRepository
    {
        public List<Payment> UpdatedPayments { get; } = [];

        public Task AddAsync(Payment payment, CancellationToken ct = default) => Task.CompletedTask;

        public Task UpdateAsync(Payment payment, CancellationToken ct = default)
        {
            UpdatedPayments.Add(payment);
            return Task.CompletedTask;
        }

        public Task<Payment?> GetByIdAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(expiredPayments.FirstOrDefault(p => p.Id == id));

        public Task<Payment?> GetByChannelReferenceAsync(string channelReference, CancellationToken ct = default) =>
            Task.FromResult(expiredPayments.FirstOrDefault(p => p.ChannelReference == channelReference));

        public Task<IReadOnlyList<Payment>> GetExpiredThreeDsPaymentsAsync(DateTimeOffset now, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Payment>>(expiredPayments);
    }
}
