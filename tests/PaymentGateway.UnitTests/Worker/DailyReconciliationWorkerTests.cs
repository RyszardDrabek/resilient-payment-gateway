using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PaymentGateway.Application.Payouts;
using PaymentGateway.Application.Reconciliation;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Ports;
using PaymentGateway.Worker;
using Xunit;

namespace PaymentGateway.UnitTests.Worker;

public sealed class DailyReconciliationWorkerTests
{
    private sealed class FakePaymentRepository(IReadOnlyList<Payment> payments) : IPaymentRepository
    {
        public Task AddAsync(Payment payment, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Payment payment, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Payment?> GetByIdAsync(string id, CancellationToken ct = default) => Task.FromResult<Payment?>(null);
        public Task<Payment?> GetByChannelReferenceAsync(string channelReference, CancellationToken ct = default) => Task.FromResult<Payment?>(null);
        public Task<IReadOnlyList<Payment>> GetCapturedPaymentsAsync(DateTimeOffset from, DateTimeOffset to, string? partyId = null, string? currency = null, CancellationToken ct = default) =>
            Task.FromResult(payments);
    }

    private sealed class FakeMediator : IMediator
    {
        public List<object> SentRequests { get; } = [];

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            SentRequests.Add(request);

            if (request is RunReconciliationCommand)
            {
                var result = new RunReconciliationResult(TotalProcessed: 5, MatchedCount: 4, DiscrepancyCount: 1);
                return Task.FromResult((TResponse)(object)result);
            }

            if (request is GeneratePayoutBatchCommand cmd)
            {
                var item = new PayoutItemDto("pi_1", "pay_1", 5000, 70, 4930, cmd.Currency, DateTimeOffset.UtcNow);
                var batch = new MerchantPayoutBatchDto(
                    "payout_1", cmd.MerchantId, cmd.PeriodStart, cmd.PeriodEnd, 5000, 70, 4930, cmd.Currency, "Completed", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, [item]);
                return Task.FromResult((TResponse)(object)batch);
            }

            throw new NotImplementedException();
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest =>
            throw new NotImplementedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;
    }

    [Fact]
    public async Task ProcessReconciliationCycleAsync_OrchestratesReconciliationAndPayoutBatches()
    {
        var payment1 = new Payment("pay_1", "merch_1", 5000, "EUR", "ADYEN", PaymentState.Captured, channelReference: "psp_1", capturedAmount: 5000);
        var payment2 = new Payment("pay_2", "merch_2", 8000, "USD", "ADYEN", PaymentState.Captured, channelReference: "psp_2", capturedAmount: 8000);

        var fakePaymentRepo = new FakePaymentRepository([payment1, payment2]);
        var fakeMediator = new FakeMediator();

        var services = new ServiceCollection();
        services.AddScoped<IPaymentRepository>(_ => fakePaymentRepo);
        services.AddScoped<IMediator>(_ => fakeMediator);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var worker = new DailyReconciliationWorker(scopeFactory, NullLogger<DailyReconciliationWorker>.Instance);

        var result = await worker.ProcessReconciliationCycleAsync();

        result.ReconciledCount.Should().Be(5);
        result.DiscrepancyCount.Should().Be(1);
        result.BatchesGenerated.Should().Be(2);

        fakeMediator.SentRequests.OfType<RunReconciliationCommand>().Should().ContainSingle();
        fakeMediator.SentRequests.OfType<GeneratePayoutBatchCommand>().Should().HaveCount(2);
    }
}
