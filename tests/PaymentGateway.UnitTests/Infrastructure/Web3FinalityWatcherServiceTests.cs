using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.Payments;
using PaymentGateway.Application.Web3;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Ports;
using PaymentGateway.Infrastructure.Web3;

namespace PaymentGateway.UnitTests.Infrastructure;

public sealed class Web3FinalityWatcherServiceTests
{
    private sealed class FakePaymentRepository : IPaymentRepository
    {
        public readonly Dictionary<string, Payment> Payments = new();
        public int UpdateCount { get; private set; }

        public Task AddAsync(Payment payment, CancellationToken ct = default)
        {
            Payments[payment.Id] = payment;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Payment payment, CancellationToken ct = default)
        {
            UpdateCount++;
            Payments[payment.Id] = payment;
            return Task.CompletedTask;
        }

        public Task<Payment?> GetByIdAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(Payments.TryGetValue(id, out var p) ? p : null);

        public Task<Payment?> GetByChannelReferenceAsync(string channelReference, CancellationToken ct = default) =>
            Task.FromResult(Payments.Values.FirstOrDefault(p => p.ChannelReference == channelReference));

        public Task<IReadOnlyList<Payment>> GetUnresolvedAsync(CancellationToken ct = default)
        {
            IReadOnlyList<Payment> list = Payments.Values
                .Where(p => p.State is PaymentState.Pending or PaymentState.Unknown)
                .ToList();
            return Task.FromResult(list);
        }
    }

    private sealed class FakeChainClient : IWeb3ChainClient
    {
        public readonly Dictionary<string, Web3TransactionReceipt> Receipts = new();

        public Task<string> BroadcastTransferAsync(string fromAddress, string toAddress, long amount, string asset, string? idempotencyToken = null, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<Web3TransactionReceipt?> GetReceiptAsync(string transactionHash, CancellationToken ct = default) =>
            Task.FromResult(Receipts.TryGetValue(transactionHash, out var r) ? r : null);

        public Task<bool> IsFinalizedAsync(string transactionHash, int requiredConfirmations, CancellationToken ct = default) =>
            Task.FromResult(Receipts.TryGetValue(transactionHash, out var r) && r.IsSuccess && r.Confirmations >= requiredConfirmations);
    }

    private sealed class FakeMediator(IPaymentRepository paymentRepository) : IMediator
    {
        public readonly List<ApplyAcquirerOutcomeCommand> DispatchedCommands = new();

        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is ApplyAcquirerOutcomeCommand cmd)
            {
                DispatchedCommands.Add(cmd);
                var handler = new ApplyAcquirerOutcomeCommandHandler(paymentRepository);
                var res = await handler.Handle(cmd, cancellationToken);
                return (TResponse)(object)res;
            }
            throw new NotImplementedException();
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotImplementedException();
    }

    private readonly Web3Options _options = new()
    {
        RequiredFinalityConfirmations = 3,
        TreasuryAddress = "0xf39Fd6e51aad88F6F4ce6aB8827279cffFb92266",
        MerchantDestinationAddress = "0x70997970C51812dc3A010C7d01b50e0d17dc79C8",
        RefundDestinationAddress = "0x3C44CdDdB6a900fa2b585dd299e03d12FA4293BC"
    };

    [Fact]
    public async Task AC1_WhenConfirmationsLessThanRequired_PaymentRemainsPending_AndDoesNotCompleteTransition()
    {
        var repo = new FakePaymentRepository();
        var chainClient = new FakeChainClient();
        var mediator = new FakeMediator(repo);

        var txHash = "0xabc123456789";
        var payment = Payment.CreatePending("cust_1", 1000000L, "USDC", "WEB3", txHash, "capture");
        await repo.AddAsync(payment);

        chainClient.Receipts[txHash] = new Web3TransactionReceipt(
            TransactionHash: txHash,
            From: _options.TreasuryAddress,
            To: _options.MerchantDestinationAddress,
            Amount: 1000000L,
            Asset: "USDC",
            IsSuccess: true,
            Confirmations: 1, // < 3 required
            BlockTimestamp: DateTimeOffset.UtcNow);

        var sut = new Web3FinalityWatcherService(
            repo,
            chainClient,
            Options.Create(_options),
            mediator,
            NullLogger<Web3FinalityWatcherService>.Instance);

        // Act
        var observation = await sut.ObserveAndApplyFinalityAsync(payment.Id);

        // Assert
        observation.Status.Should().Be(Web3SettlementStatus.Pending);
        observation.ConfirmationDepth.Should().Be(1);
        observation.RequiredConfirmations.Should().Be(3);
        payment.State.Should().Be(PaymentState.Pending);
        mediator.DispatchedCommands.Should().BeEmpty();
    }

    [Fact]
    public async Task AC2_WhenConfirmationsMeetRequired_TransitionsToCaptured_AndRetainsTxHash()
    {
        var repo = new FakePaymentRepository();
        var chainClient = new FakeChainClient();
        var mediator = new FakeMediator(repo);

        var txHash = "0xfinalized_capture";
        var payment = Payment.CreatePending("cust_1", 2500000L, "USDC", "WEB3", txHash, "capture");
        await repo.AddAsync(payment);

        chainClient.Receipts[txHash] = new Web3TransactionReceipt(
            TransactionHash: txHash,
            From: _options.TreasuryAddress,
            To: _options.MerchantDestinationAddress,
            Amount: 2500000L,
            Asset: "USDC",
            IsSuccess: true,
            Confirmations: 3, // >= 3
            BlockTimestamp: DateTimeOffset.UtcNow);

        var sut = new Web3FinalityWatcherService(
            repo,
            chainClient,
            Options.Create(_options),
            mediator,
            NullLogger<Web3FinalityWatcherService>.Instance);

        // Act
        var observation = await sut.ObserveAndApplyFinalityAsync(payment.Id);

        // Assert
        observation.Status.Should().Be(Web3SettlementStatus.Settled);
        observation.ConfirmationDepth.Should().Be(3);
        observation.TransactionHash.Should().Be(txHash);

        mediator.DispatchedCommands.Should().ContainSingle();
        var cmd = mediator.DispatchedCommands[0];
        cmd.PaymentId.Should().Be(payment.Id);
        cmd.Outcome.Should().Be(PaymentLifecycleOutcome.Captured);
        cmd.ChannelReference.Should().Be(txHash);

        payment.State.Should().Be(PaymentState.Captured);
    }

    [Fact]
    public async Task AC2_WhenRefundConfirmationsMeetRequired_TransitionsToRefunded_AndRetainsTxHash()
    {
        var repo = new FakePaymentRepository();
        var chainClient = new FakeChainClient();
        var mediator = new FakeMediator(repo);

        var txHash = "0xfinalized_refund";
        var payment = Payment.CreatePending("cust_1", 1000000L, "USDC", "WEB3", txHash, "refund");
        await repo.AddAsync(payment);

        chainClient.Receipts[txHash] = new Web3TransactionReceipt(
            TransactionHash: txHash,
            From: _options.TreasuryAddress,
            To: _options.RefundDestinationAddress,
            Amount: 1000000L,
            Asset: "USDC",
            IsSuccess: true,
            Confirmations: 4, // >= 3
            BlockTimestamp: DateTimeOffset.UtcNow);

        var sut = new Web3FinalityWatcherService(
            repo,
            chainClient,
            Options.Create(_options),
            mediator,
            NullLogger<Web3FinalityWatcherService>.Instance);

        // Act
        var observation = await sut.ObserveAndApplyFinalityAsync(payment.Id);

        // Assert
        observation.Status.Should().Be(Web3SettlementStatus.Settled);
        observation.TransactionHash.Should().Be(txHash);

        mediator.DispatchedCommands.Should().ContainSingle();
        var cmd = mediator.DispatchedCommands[0];
        cmd.PaymentId.Should().Be(payment.Id);
        cmd.Outcome.Should().Be(PaymentLifecycleOutcome.Refunded);
        cmd.ChannelReference.Should().Be(txHash);

        payment.State.Should().Be(PaymentState.Refunded);
    }

    [Fact]
    public async Task AC3_WhenTransactionDroppedOrReorganized_KeepsPaymentPending_WithReorgPendingStatus()
    {
        var repo = new FakePaymentRepository();
        var chainClient = new FakeChainClient();
        var mediator = new FakeMediator(repo);

        var txHash = "0xreorged_tx";
        var payment = Payment.CreatePending("cust_reorg", 1000000L, "USDC", "WEB3", txHash, "capture");
        await repo.AddAsync(payment);

        // Receipt is not in chainClient (dropped / reorged)

        var sut = new Web3FinalityWatcherService(
            repo,
            chainClient,
            Options.Create(_options),
            mediator,
            NullLogger<Web3FinalityWatcherService>.Instance);

        // Act
        var observation = await sut.ObserveAndApplyFinalityAsync(payment.Id);

        // Assert
        observation.Status.Should().Be(Web3SettlementStatus.ReorgPending);
        observation.ConfirmationDepth.Should().Be(0);
        payment.State.Should().Be(PaymentState.Pending);
        mediator.DispatchedCommands.Should().BeEmpty();
    }

    [Fact]
    public async Task AC4_SettledRequiresFinalitySatisfied_NotJustBroadcast()
    {
        var repo = new FakePaymentRepository();
        var chainClient = new FakeChainClient();
        var mediator = new FakeMediator(repo);

        var txHash = "0xbroadcast_only";
        var payment = Payment.CreatePending("cust_1", 1000000L, "USDC", "WEB3", txHash, "capture");
        await repo.AddAsync(payment);

        chainClient.Receipts[txHash] = new Web3TransactionReceipt(
            TransactionHash: txHash,
            From: _options.TreasuryAddress,
            To: _options.MerchantDestinationAddress,
            Amount: 1000000L,
            Asset: "USDC",
            IsSuccess: true,
            Confirmations: 0, // Broadcast only, 0 confirmations
            BlockTimestamp: DateTimeOffset.UtcNow);

        var sut = new Web3FinalityWatcherService(
            repo,
            chainClient,
            Options.Create(_options),
            mediator,
            NullLogger<Web3FinalityWatcherService>.Instance);

        // Act
        var observation = await sut.ObserveAndApplyFinalityAsync(payment.Id);

        // Assert: is NOT settled
        observation.Status.Should().NotBe(Web3SettlementStatus.Settled);
        observation.Status.Should().Be(Web3SettlementStatus.Pending);
        payment.State.Should().Be(PaymentState.Pending);
        mediator.DispatchedCommands.Should().BeEmpty();
    }

    [Fact]
    public async Task WhenOnChainTransactionReverts_RestoresPriorState()
    {
        var repo = new FakePaymentRepository();
        var chainClient = new FakeChainClient();
        var mediator = new FakeMediator(repo);

        var txHash = "0xreverted_tx";
        var payment = Payment.CreatePending("cust_1", 1000000L, "USDC", "WEB3", txHash, "capture");
        await repo.AddAsync(payment);

        chainClient.Receipts[txHash] = new Web3TransactionReceipt(
            TransactionHash: txHash,
            From: _options.TreasuryAddress,
            To: _options.MerchantDestinationAddress,
            Amount: 1000000L,
            Asset: "USDC",
            IsSuccess: false, // Reverted!
            Confirmations: 5,
            BlockTimestamp: DateTimeOffset.UtcNow);

        var sut = new Web3FinalityWatcherService(
            repo,
            chainClient,
            Options.Create(_options),
            mediator,
            NullLogger<Web3FinalityWatcherService>.Instance);

        // Act
        var observation = await sut.ObserveAndApplyFinalityAsync(payment.Id);

        // Assert
        observation.Status.Should().Be(Web3SettlementStatus.Settled);

        // Restores Authorized (prior state for capture)
        mediator.DispatchedCommands.Should().ContainSingle();
        var cmd = mediator.DispatchedCommands[0];
        cmd.Outcome.Should().Be(PaymentLifecycleOutcome.Authorized);
        payment.State.Should().Be(PaymentState.Authorized);
    }

    [Fact]
    public async Task WatchAllPendingSettlements_ProcessesOnlyWeb3PendingPayments()
    {
        var repo = new FakePaymentRepository();
        var chainClient = new FakeChainClient();
        var mediator = new FakeMediator(repo);

        var p1 = Payment.CreatePending("c1", 1000L, "USDC", "WEB3", "0xtx1", "capture");
        var p2 = Payment.CreatePending("c2", 2000L, "EUR", "ADYEN", "psp_2", "capture");
        var p3 = Payment.CreatePending("c3", 3000L, "USDC", "WEB3", "0xtx3", "refund");

        await repo.AddAsync(p1);
        await repo.AddAsync(p2);
        await repo.AddAsync(p3);

        chainClient.Receipts["0xtx1"] = new Web3TransactionReceipt("0xtx1", _options.TreasuryAddress, _options.MerchantDestinationAddress, 1000L, "USDC", true, 3, DateTimeOffset.UtcNow);
        chainClient.Receipts["0xtx3"] = new Web3TransactionReceipt("0xtx3", _options.TreasuryAddress, _options.RefundDestinationAddress, 3000L, "USDC", true, 1, DateTimeOffset.UtcNow);

        var sut = new Web3FinalityWatcherService(
            repo,
            chainClient,
            Options.Create(_options),
            mediator,
            NullLogger<Web3FinalityWatcherService>.Instance);

        // Act
        var observations = await sut.WatchAllPendingSettlementsAsync();

        // Assert: only p1 and p3 processed (Web3)
        observations.Should().HaveCount(2);
        observations.Should().Contain(o => o.PaymentId == p1.Id && o.Status == Web3SettlementStatus.Settled);
        observations.Should().Contain(o => o.PaymentId == p3.Id && o.Status == Web3SettlementStatus.Pending);
    }
}
