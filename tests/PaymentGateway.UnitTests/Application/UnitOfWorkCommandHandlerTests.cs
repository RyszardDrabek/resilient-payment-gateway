using System.Collections.Concurrent;
using FluentAssertions;
using PaymentGateway.Application.Payments;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Exceptions;
using PaymentGateway.Domain.Ports;
using Xunit;

namespace PaymentGateway.UnitTests.Application;

public sealed class UnitOfWorkCommandHandlerTests
{
    private sealed class TrackingUnitOfWork : IUnitOfWork
    {
        public int TransactionCount { get; private set; }
        public int CommitCount { get; private set; }
        public int RollbackCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);

        public async Task ExecuteTransactionAsync(Func<CancellationToken, Task> action, CancellationToken ct = default)
        {
            TransactionCount++;
            try
            {
                await action(ct);
                CommitCount++;
            }
            catch
            {
                RollbackCount++;
                throw;
            }
        }

        public async Task<T> ExecuteTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct = default)
        {
            TransactionCount++;
            try
            {
                var result = await action(ct);
                CommitCount++;
                return result;
            }
            catch
            {
                RollbackCount++;
                throw;
            }
        }
    }

    private sealed class InMemoryPaymentRepository : IPaymentRepository
    {
        public readonly ConcurrentDictionary<string, Payment> Payments = new();
        public bool ShouldThrowOnAdd { get; set; }
        public bool ShouldThrowOnUpdate { get; set; }

        public Task AddAsync(Payment payment, CancellationToken ct = default)
        {
            if (ShouldThrowOnAdd)
            {
                throw new InvalidOperationException("Simulated database failure during payment persistence.");
            }

            Payments[payment.Id] = payment;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Payment payment, CancellationToken ct = default)
        {
            if (ShouldThrowOnUpdate)
            {
                throw new InvalidOperationException("Simulated database failure during payment update.");
            }

            Payments[payment.Id] = payment;
            return Task.CompletedTask;
        }

        public Task<Payment?> GetByIdAsync(string id, CancellationToken ct = default)
        {
            Payments.TryGetValue(id, out var payment);
            return Task.FromResult(payment);
        }

        public Task<Payment?> GetByChannelReferenceAsync(string channelReference, CancellationToken ct = default) => Task.FromResult<Payment?>(null);
        public Task<IReadOnlyList<Payment>> GetUnresolvedAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Payment>>(new List<Payment>());
        public Task<(IReadOnlyList<Payment> Items, int TotalCount)> GetUnresolvedPaginatedAsync(
            DateTimeOffset createdBefore, string? channel = null, int page = 1, int pageSize = 50, CancellationToken ct = default) =>
            Task.FromResult<(IReadOnlyList<Payment> Items, int TotalCount)>((new List<Payment>(), 0));
    }

    private sealed class InMemoryIdempotencyRepository : IIdempotencyRepository
    {
        public readonly ConcurrentDictionary<string, IdempotencyRecord> Records = new();

        public Task<IdempotencyRecord?> FindAsync(string key, string commandName, string? paymentId = null, CancellationToken ct = default)
        {
            Records.TryGetValue(key, out var record);
            return Task.FromResult(record);
        }

        public Task AddAsync(IdempotencyRecord record, CancellationToken ct = default)
        {
            Records[record.Key] = record;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(IdempotencyRecord record, CancellationToken ct = default)
        {
            Records[record.Key] = record;
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryFxQuoteRepository : IFxQuoteRepository
    {
        public readonly ConcurrentDictionary<string, CurrencyQuote> Quotes = new();

        public Task<CurrencyQuote?> GetByIdAsync(string quoteId, CancellationToken ct = default)
        {
            Quotes.TryGetValue(quoteId, out var quote);
            return Task.FromResult(quote);
        }

        public Task AddAsync(CurrencyQuote quote, CancellationToken ct = default)
        {
            Quotes[quote.Id] = quote;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(CurrencyQuote quote, CancellationToken ct = default)
        {
            Quotes[quote.Id] = quote;
            return Task.CompletedTask;
        }
    }

    private sealed class StubSettlementPort : ISettlementPort
    {
        public Task<SettlementResult> AuthorizeAsync(
            string partyId, long amount, string currency, string? channel = null, CancellationToken ct = default) =>
            Task.FromResult(SettlementResult.Success("MOCK", $"ch_ref_{Guid.NewGuid():N}"));

        public Task<SettlementResult> CaptureAsync(string paymentId, string channelReference, long amount, string currency, string? channel = null, CancellationToken ct = default) =>
            Task.FromResult(SettlementResult.Success("MOCK", channelReference));

        public Task<SettlementResult> RefundAsync(string paymentId, string channelReference, long amount, string currency, string? channel = null, CancellationToken ct = default) =>
            Task.FromResult(SettlementResult.Success("MOCK", channelReference));

        public Task<SettlementResult> CancelAsync(string paymentId, string channelReference, string? channel = null, CancellationToken ct = default) =>
            Task.FromResult(SettlementResult.Success("MOCK", channelReference));

        public Task<SettlementResult> SubmitThreeDsDetailsAsync(string paymentId, string channelReference, string? threeDsResult, IReadOnlyDictionary<string, string>? details = null, string? channel = null, CancellationToken ct = default) =>
            Task.FromResult(SettlementResult.Success("MOCK", channelReference));
    }

    [Fact]
    public async Task AuthorizePayment_WithUnitOfWork_ExecutesTransactionAndCommits()
    {
        var paymentRepo = new InMemoryPaymentRepository();
        var idemRepo = new InMemoryIdempotencyRepository();
        var settlementPort = new StubSettlementPort();
        var uow = new TrackingUnitOfWork();

        var handler = new AuthorizePaymentCommandHandler(
            settlementPort,
            paymentRepo,
            idemRepo,
            unitOfWork: uow);

        var command = new AuthorizePaymentCommand(
            IdempotencyKey: "idem_uow_auth",
            PartyId: "merchant_uow",
            Amount: 10000,
            Currency: "EUR",
            SettlementChannel: "MOCK");

        var result = await handler.Handle(command, CancellationToken.None);

        result.Should().NotBeNull();
        result.State.Should().Be("Authorized");
        uow.TransactionCount.Should().Be(1);
        uow.CommitCount.Should().Be(1);
        uow.RollbackCount.Should().Be(0);
    }

    [Fact]
    public async Task AuthorizePayment_WhenPersistenceThrows_RollsBackUnitOfWorkTransaction()
    {
        var paymentRepo = new InMemoryPaymentRepository { ShouldThrowOnAdd = true };
        var idemRepo = new InMemoryIdempotencyRepository();
        var settlementPort = new StubSettlementPort();
        var uow = new TrackingUnitOfWork();

        var handler = new AuthorizePaymentCommandHandler(
            settlementPort,
            paymentRepo,
            idemRepo,
            unitOfWork: uow);

        var command = new AuthorizePaymentCommand(
            IdempotencyKey: "idem_uow_fail",
            PartyId: "merchant_uow",
            Amount: 10000,
            Currency: "EUR",
            SettlementChannel: "MOCK");

        var act = () => handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Simulated database failure*");

        uow.TransactionCount.Should().Be(1);
        uow.CommitCount.Should().Be(0);
        uow.RollbackCount.Should().Be(1);
    }

    [Fact]
    public async Task AuthorizePayment_WithQuoteAndUnitOfWork_AtomicallyPersistsAllAggregates()
    {
        var now = DateTimeOffset.UtcNow;
        var quote = CurrencyQuote.Create(
            id: "fxq_uow_test",
            partyId: "merchant_uow",
            baseCurrency: "PLN",
            baseAmount: 10000,
            targetCurrency: "USDC",
            spotRate: 0.2520m,
            spreadBps: 50,
            ttl: TimeSpan.FromMinutes(15),
            now: now);

        var quoteRepo = new InMemoryFxQuoteRepository();
        await quoteRepo.AddAsync(quote);

        var paymentRepo = new InMemoryPaymentRepository();
        var idemRepo = new InMemoryIdempotencyRepository();
        var settlementPort = new StubSettlementPort();
        var uow = new TrackingUnitOfWork();

        var handler = new AuthorizePaymentCommandHandler(
            settlementPort,
            paymentRepo,
            idemRepo,
            fxQuoteRepository: quoteRepo,
            unitOfWork: uow);

        var command = new AuthorizePaymentCommand(
            IdempotencyKey: "idem_quote_uow_success",
            PartyId: "merchant_uow",
            Amount: quote.TargetAmount,
            Currency: quote.TargetCurrency,
            SettlementChannel: "MOCK",
            QuoteId: quote.Id);

        var result = await handler.Handle(command, CancellationToken.None);

        result.Should().NotBeNull();
        result.QuoteId.Should().Be(quote.Id);
        uow.TransactionCount.Should().Be(1);
        uow.CommitCount.Should().Be(1);

        var quoteInRepo = await quoteRepo.GetByIdAsync(quote.Id);
        quoteInRepo!.Status.Should().Be(CurrencyQuoteStatus.Consumed);
    }

    [Fact]
    public async Task CapturePayment_WithUnitOfWork_ExecutesTransactionAndCommits()
    {
        var payment = Payment.Authorize("merchant_uow", 10000, "EUR", "MOCK", "ch_ref_cap");
        var paymentRepo = new InMemoryPaymentRepository();
        await paymentRepo.AddAsync(payment);

        var idemRepo = new InMemoryIdempotencyRepository();
        var settlementPort = new StubSettlementPort();
        var uow = new TrackingUnitOfWork();

        var handler = new CapturePaymentCommandHandler(
            paymentRepo,
            settlementPort,
            idemRepo,
            unitOfWork: uow);

        var command = new CapturePaymentCommand(payment.Id, "idem_uow_cap", 10000);
        var result = await handler.Handle(command, CancellationToken.None);

        result.Should().NotBeNull();
        result.State.Should().Be("Captured");
        uow.TransactionCount.Should().Be(1);
        uow.CommitCount.Should().Be(1);
        uow.RollbackCount.Should().Be(0);
    }

    [Fact]
    public async Task CapturePayment_WhenPersistenceThrows_RollsBackUnitOfWorkTransaction()
    {
        var payment = Payment.Authorize("merchant_uow", 10000, "EUR", "MOCK", "ch_ref_cap_fail");
        var paymentRepo = new InMemoryPaymentRepository();
        await paymentRepo.AddAsync(payment);
        paymentRepo.ShouldThrowOnUpdate = true;

        var idemRepo = new InMemoryIdempotencyRepository();
        var settlementPort = new StubSettlementPort();
        var uow = new TrackingUnitOfWork();

        var handler = new CapturePaymentCommandHandler(
            paymentRepo,
            settlementPort,
            idemRepo,
            unitOfWork: uow);

        var command = new CapturePaymentCommand(payment.Id, "idem_uow_cap_fail", 10000);
        var act = () => handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Simulated database failure during payment update*");

        uow.TransactionCount.Should().Be(1);
        uow.CommitCount.Should().Be(0);
        uow.RollbackCount.Should().Be(1);
    }

    [Fact]
    public async Task RefundPayment_WithUnitOfWork_ExecutesTransactionAndCommits()
    {
        var payment = Payment.Authorize("merchant_uow", 10000, "EUR", "MOCK", "ch_ref_ref");
        payment.Capture("ch_ref_ref", 10000);

        var paymentRepo = new InMemoryPaymentRepository();
        await paymentRepo.AddAsync(payment);

        var idemRepo = new InMemoryIdempotencyRepository();
        var settlementPort = new StubSettlementPort();
        var uow = new TrackingUnitOfWork();

        var handler = new RefundPaymentCommandHandler(
            paymentRepo,
            settlementPort,
            idemRepo,
            unitOfWork: uow);

        var command = new RefundPaymentCommand(payment.Id, "idem_uow_ref", 5000);
        var result = await handler.Handle(command, CancellationToken.None);

        result.Should().NotBeNull();
        result.State.Should().Be("PartiallyRefunded");
        uow.TransactionCount.Should().Be(1);
        uow.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task CancelPayment_WithUnitOfWork_ExecutesTransactionAndCommits()
    {
        var payment = Payment.Authorize("merchant_uow", 10000, "EUR", "MOCK", "ch_ref_cnc");
        var paymentRepo = new InMemoryPaymentRepository();
        await paymentRepo.AddAsync(payment);

        var idemRepo = new InMemoryIdempotencyRepository();
        var settlementPort = new StubSettlementPort();
        var uow = new TrackingUnitOfWork();

        var handler = new CancelPaymentCommandHandler(
            paymentRepo,
            settlementPort,
            idemRepo,
            unitOfWork: uow);

        var command = new CancelPaymentCommand(payment.Id, "idem_uow_cnc");
        var result = await handler.Handle(command, CancellationToken.None);

        result.Should().NotBeNull();
        result.State.Should().Be("Cancelled");
        uow.TransactionCount.Should().Be(1);
        uow.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task SubmitThreeDsResult_WithUnitOfWork_ExecutesTransactionAndCommits()
    {
        var action = new ThreeDsActionDetails("threeDS2", "https://challenge.bank.com", "tok_3ds");
        var payment = Payment.CreateRequiresAction("merchant_uow", 10000, "EUR", "MOCK", "ch_ref_3ds", action);
        var paymentRepo = new InMemoryPaymentRepository();
        await paymentRepo.AddAsync(payment);

        var settlementPort = new StubSettlementPort();
        var uow = new TrackingUnitOfWork();

        var handler = new SubmitThreeDsResultCommandHandler(
            paymentRepo,
            settlementPort,
            unitOfWork: uow);

        var command = new SubmitThreeDsResultCommand(payment.Id, "Y", new Dictionary<string, string>());
        var result = await handler.Handle(command, CancellationToken.None);

        result.Should().NotBeNull();
        result.State.Should().Be("Authorized");
        uow.TransactionCount.Should().Be(1);
        uow.CommitCount.Should().Be(1);
    }
}
