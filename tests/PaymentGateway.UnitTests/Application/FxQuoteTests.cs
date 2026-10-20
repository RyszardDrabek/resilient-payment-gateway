using System.Collections.Concurrent;
using FluentAssertions;
using PaymentGateway.Application.Fx.Commands;
using PaymentGateway.Application.Fx.Models;
using PaymentGateway.Application.Fx.Ports;
using PaymentGateway.Application.Fx.Queries;
using PaymentGateway.Application.Payments;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Exceptions;
using PaymentGateway.Domain.Ports;
using Xunit;

namespace PaymentGateway.UnitTests.Application;

public sealed class FxQuoteTests
{
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

    private sealed class StubFxRateProvider(decimal rate = 0.2520m) : IFxRateProvider
    {
        public Task<decimal> GetSpotRateAsync(string baseCurrency, string targetCurrency, CancellationToken ct = default) =>
            Task.FromResult(rate);

        public Task<IReadOnlyDictionary<string, decimal>> GetRatesAsync(string baseCurrency, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<string, decimal>>(new Dictionary<string, decimal> { ["USDC"] = rate });
    }

    private sealed class StubSettlementPort : ISettlementPort
    {
        public Task<SettlementResult> AuthorizeAsync(
            string partyId,
            long amount,
            string currency,
            string? channel = null,
            CancellationToken ct = default)
        {
            return Task.FromResult(SettlementResult.Success("MOCK", $"ch_ref_{Guid.NewGuid():N}"));
        }

        public Task<SettlementResult> CaptureAsync(string paymentId, string channelReference, long amount, string currency, string? channel = null, CancellationToken ct = default) =>
            Task.FromResult(SettlementResult.Success("MOCK", channelReference));

        public Task<SettlementResult> CancelAsync(string paymentId, string channelReference, string? channel = null, CancellationToken ct = default) =>
            Task.FromResult(SettlementResult.Success("MOCK", channelReference));

        public Task<SettlementResult> RefundAsync(string paymentId, string channelReference, long amount, string currency, string? channel = null, CancellationToken ct = default) =>
            Task.FromResult(SettlementResult.Success("MOCK", channelReference));
    }

    private sealed class InMemoryPaymentRepository : IPaymentRepository
    {
        public readonly ConcurrentDictionary<string, Payment> Payments = new();

        public Task<Payment?> GetByIdAsync(string id, CancellationToken ct = default)
        {
            Payments.TryGetValue(id, out var payment);
            return Task.FromResult(payment);
        }

        public Task<Payment?> GetByChannelReferenceAsync(string channelReference, CancellationToken ct = default)
        {
            var payment = Payments.Values.FirstOrDefault(p => p.ChannelReference == channelReference);
            return Task.FromResult(payment);
        }

        public Task AddAsync(Payment payment, CancellationToken ct = default)
        {
            Payments[payment.Id] = payment;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Payment payment, CancellationToken ct = default)
        {
            Payments[payment.Id] = payment;
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryIdempotencyRepository : IIdempotencyRepository
    {
        public readonly ConcurrentDictionary<string, IdempotencyRecord> Records = new();

        public Task<IdempotencyRecord?> FindAsync(string key, string commandType, string? paymentId, CancellationToken ct = default)
        {
            Records.TryGetValue($"{key}:{commandType}", out var record);
            return Task.FromResult(record);
        }

        public Task AddAsync(IdempotencyRecord record, CancellationToken ct = default)
        {
            Records[$"{record.Key}:{record.CommandType}"] = record;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(IdempotencyRecord record, CancellationToken ct = default)
        {
            Records[$"{record.Key}:{record.CommandType}"] = record;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task CreateFxQuote_ReturnsValidQuoteDto_AndSavesToRepository()
    {
        var repo = new InMemoryFxQuoteRepository();
        var rateProvider = new StubFxRateProvider(0.2520m);
        var handler = new CreateFxQuoteCommandHandler(rateProvider, repo, new FxOptions { DefaultSpreadBps = 50, DefaultTtlMinutes = 15 });

        var command = new CreateFxQuoteCommand("PLN", 10000, "USDC", "merchant_1", 15);
        var dto = await handler.Handle(command, CancellationToken.None);

        dto.Should().NotBeNull();
        dto.BaseCurrency.Should().Be("PLN");
        dto.BaseAmount.Should().Be(10000);
        dto.TargetCurrency.Should().Be("USDC");
        dto.TargetAmount.Should().Be(2507); // 10000 * (0.2520 * (1 - 0.005))
        dto.SpotRate.Should().Be(0.2520m);
        dto.SpreadBps.Should().Be(50);
        dto.Status.Should().Be("Active");
        dto.RemainingSeconds.Should().BeGreaterThan(0);

        repo.Quotes.Should().ContainKey(dto.QuoteId);
    }

    [Fact]
    public async Task GetFxQuoteById_ReturnsNull_WhenNotFound()
    {
        var repo = new InMemoryFxQuoteRepository();
        var handler = new GetFxQuoteByIdQueryHandler(repo);

        var result = await handler.Handle(new GetFxQuoteByIdQuery("fxq_missing"), CancellationToken.None);
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetFxQuoteById_ExpiresQuote_WhenNowPastExpiresAt()
    {
        var repo = new InMemoryFxQuoteRepository();
        var now = DateTimeOffset.UtcNow.AddMinutes(-20);
        var quote = CurrencyQuote.Create("fxq_old", "m1", "EUR", 5000, "USDC", 1.0850m, 50, TimeSpan.FromMinutes(15), now);
        await repo.AddAsync(quote);

        var handler = new GetFxQuoteByIdQueryHandler(repo);
        var result = await handler.Handle(new GetFxQuoteByIdQuery("fxq_old"), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Status.Should().Be("Expired");
        repo.Quotes["fxq_old"].Status.Should().Be(CurrencyQuoteStatus.Expired);
    }

    [Fact]
    public async Task AuthorizePayment_WithValidQuote_SetsQuoteDetailsAndConsumesQuote()
    {
        var quoteRepo = new InMemoryFxQuoteRepository();
        var paymentRepo = new InMemoryPaymentRepository();
        var idemRepo = new InMemoryIdempotencyRepository();
        var settlementPort = new StubSettlementPort();

        var now = DateTimeOffset.UtcNow;
        var quote = CurrencyQuote.Create("fxq_valid", "m1", "PLN", 10000, "USDC", 0.2520m, 50, TimeSpan.FromMinutes(15), now);
        await quoteRepo.AddAsync(quote);

        var handler = new AuthorizePaymentCommandHandler(
            settlementPort,
            paymentRepo,
            idemRepo,
            fxQuoteRepository: quoteRepo);

        var command = new AuthorizePaymentCommand(
            IdempotencyKey: "idem_quote_1",
            PartyId: "m1",
            Amount: quote.TargetAmount,
            Currency: "USDC",
            SettlementChannel: "MOCK",
            QuoteId: quote.Id);

        var result = await handler.Handle(command, CancellationToken.None);

        result.Should().NotBeNull();
        result.State.Should().Be("Authorized");
        result.QuoteId.Should().Be(quote.Id);
        result.OriginalAmount.Should().Be(10000);
        result.OriginalCurrency.Should().Be("PLN");
        result.ExchangeRate.Should().Be(quote.ExchangeRate);

        var savedPayment = paymentRepo.Payments[result.PaymentId];
        savedPayment.QuoteId.Should().Be(quote.Id);
        savedPayment.OriginalAmount.Should().Be(10000);
        savedPayment.OriginalCurrency.Should().Be("PLN");

        var updatedQuote = quoteRepo.Quotes[quote.Id];
        updatedQuote.Status.Should().Be(CurrencyQuoteStatus.Consumed);
        updatedQuote.ConsumedByPaymentId.Should().Be(result.PaymentId);
    }

    [Fact]
    public async Task AuthorizePayment_WithExpiredQuote_ThrowsQuoteExpiredException()
    {
        var quoteRepo = new InMemoryFxQuoteRepository();
        var paymentRepo = new InMemoryPaymentRepository();
        var idemRepo = new InMemoryIdempotencyRepository();
        var settlementPort = new StubSettlementPort();

        var now = DateTimeOffset.UtcNow.AddMinutes(-30);
        var quote = CurrencyQuote.Create("fxq_expired", "m1", "PLN", 10000, "USDC", 0.2520m, 50, TimeSpan.FromMinutes(15), now);
        await quoteRepo.AddAsync(quote);

        var handler = new AuthorizePaymentCommandHandler(
            settlementPort,
            paymentRepo,
            idemRepo,
            fxQuoteRepository: quoteRepo);

        var command = new AuthorizePaymentCommand(
            IdempotencyKey: "idem_quote_exp",
            PartyId: "m1",
            Amount: quote.TargetAmount,
            Currency: "USDC",
            SettlementChannel: "MOCK",
            QuoteId: quote.Id);

        var act = () => handler.Handle(command, CancellationToken.None);
        await act.Should().ThrowAsync<QuoteExpiredException>()
            .WithMessage("*expired*");
    }

    [Fact]
    public async Task AuthorizePayment_WithCurrencyMismatch_ThrowsQuoteMismatchException()
    {
        var quoteRepo = new InMemoryFxQuoteRepository();
        var paymentRepo = new InMemoryPaymentRepository();
        var idemRepo = new InMemoryIdempotencyRepository();
        var settlementPort = new StubSettlementPort();

        var now = DateTimeOffset.UtcNow;
        var quote = CurrencyQuote.Create("fxq_mismatch", "m1", "PLN", 10000, "USDC", 0.2520m, 50, TimeSpan.FromMinutes(15), now);
        await quoteRepo.AddAsync(quote);

        var handler = new AuthorizePaymentCommandHandler(
            settlementPort,
            paymentRepo,
            idemRepo,
            fxQuoteRepository: quoteRepo);

        var command = new AuthorizePaymentCommand(
            IdempotencyKey: "idem_quote_mismatch",
            PartyId: "m1",
            Amount: quote.TargetAmount,
            Currency: "EUR", // Mismatch with USDC
            SettlementChannel: "MOCK",
            QuoteId: quote.Id);

        var act = () => handler.Handle(command, CancellationToken.None);
        await act.Should().ThrowAsync<QuoteMismatchException>()
            .WithMessage("*target currency*");
    }

    [Fact]
    public async Task AuthorizePayment_WithNonExistentQuote_ThrowsQuoteNotFoundException()
    {
        var quoteRepo = new InMemoryFxQuoteRepository();
        var paymentRepo = new InMemoryPaymentRepository();
        var idemRepo = new InMemoryIdempotencyRepository();
        var settlementPort = new StubSettlementPort();

        var handler = new AuthorizePaymentCommandHandler(
            settlementPort,
            paymentRepo,
            idemRepo,
            fxQuoteRepository: quoteRepo);

        var command = new AuthorizePaymentCommand(
            IdempotencyKey: "idem_quote_404",
            PartyId: "m1",
            Amount: 1000,
            Currency: "USDC",
            SettlementChannel: "MOCK",
            QuoteId: "fxq_nonexistent");

        var act = () => handler.Handle(command, CancellationToken.None);
        await act.Should().ThrowAsync<QuoteNotFoundException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public void FxQuoteMapper_ToDto_MapsAllFieldsAndCalculatesRemainingSeconds()
    {
        var now = DateTimeOffset.UtcNow;
        var quote = CurrencyQuote.Create(
            id: "fxq_mapper_test",
            partyId: "merchant_1",
            baseCurrency: "EUR",
            baseAmount: 10000,
            targetCurrency: "USDC",
            spotRate: 1.0850m,
            spreadBps: 50,
            ttl: TimeSpan.FromMinutes(10),
            now: now);

        var dto = PaymentGateway.Application.Fx.FxQuoteMapper.ToDto(quote, now.AddMinutes(2));

        dto.QuoteId.Should().Be("fxq_mapper_test");
        dto.PartyId.Should().Be("merchant_1");
        dto.BaseCurrency.Should().Be("EUR");
        dto.BaseAmount.Should().Be(10000);
        dto.TargetCurrency.Should().Be("USDC");
        dto.SpotRate.Should().Be(1.0850m);
        dto.SpreadBps.Should().Be(50);
        dto.Status.Should().Be("Active");
        dto.RemainingSeconds.Should().Be(480); // 8 minutes remaining
    }

    private sealed class FailingPaymentRepository : IPaymentRepository
    {
        public Task AddAsync(Payment payment, CancellationToken ct = default) =>
            throw new InvalidOperationException("DB error during payment persistence");

        public Task UpdateAsync(Payment payment, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Payment?> GetByIdAsync(string id, CancellationToken ct = default) => Task.FromResult<Payment?>(null);
        public Task<Payment?> GetByChannelReferenceAsync(string channelReference, CancellationToken ct = default) => Task.FromResult<Payment?>(null);
        public Task<IReadOnlyList<Payment>> GetUnresolvedAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Payment>>(new List<Payment>());
        public Task<(IReadOnlyList<Payment> Items, int TotalCount)> GetUnresolvedPaginatedAsync(
            DateTimeOffset createdBefore, string? channel = null, int page = 1, int pageSize = 50, CancellationToken ct = default) =>
            Task.FromResult<(IReadOnlyList<Payment> Items, int TotalCount)>((new List<Payment>(), 0));
    }

    [Fact]
    public async Task AuthorizePayment_WhenPaymentPersistenceFails_QuoteIsNotUpdatedAsConsumedInRepository()
    {
        var now = DateTimeOffset.UtcNow;
        var quote = CurrencyQuote.Create(
            id: "fxq_rollback_test",
            partyId: "m1",
            baseCurrency: "PLN",
            baseAmount: 10000,
            targetCurrency: "USDC",
            spotRate: 0.2520m,
            spreadBps: 50,
            ttl: TimeSpan.FromMinutes(15),
            now: now);

        var quoteRepo = new InMemoryFxQuoteRepository();
        await quoteRepo.AddAsync(quote);

        var failingPaymentRepo = new FailingPaymentRepository();
        var idemRepo = new InMemoryIdempotencyRepository();
        var settlementPort = new StubSettlementPort();

        var handler = new AuthorizePaymentCommandHandler(
            settlementPort,
            failingPaymentRepo,
            idemRepo,
            fxQuoteRepository: quoteRepo);

        var command = new AuthorizePaymentCommand(
            IdempotencyKey: "idem_quote_fail",
            PartyId: "m1",
            Amount: quote.TargetAmount,
            Currency: "USDC",
            SettlementChannel: "MOCK",
            QuoteId: quote.Id);

        var act = () => handler.Handle(command, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*DB error during payment persistence*");

        // Verify quote in repository is still Active and not marked Consumed
        var quoteInRepo = await quoteRepo.GetByIdAsync(quote.Id);
        quoteInRepo.Should().NotBeNull();
        quoteInRepo!.Status.Should().Be(CurrencyQuoteStatus.Active);
        quoteInRepo.ConsumedByPaymentId.Should().BeNull();
    }
}
