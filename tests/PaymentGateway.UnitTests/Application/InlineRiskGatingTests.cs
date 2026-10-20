using FluentAssertions;
using PaymentGateway.Application.Payments;
using PaymentGateway.Application.Risk.Models;
using PaymentGateway.Application.Risk.Ports;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Ports;
using Xunit;

namespace PaymentGateway.UnitTests.Application;

public sealed class InlineRiskGatingTests
{
    private class InMemoryPaymentRepository : IPaymentRepository
    {
        public List<Payment> SavedPayments { get; } = [];

        public Task AddAsync(Payment payment, CancellationToken ct = default)
        {
            SavedPayments.Add(payment);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Payment payment, CancellationToken ct = default)
        {
            var idx = SavedPayments.FindIndex(x => x.Id == payment.Id);
            if (idx >= 0) SavedPayments[idx] = payment;
            else SavedPayments.Add(payment);
            return Task.CompletedTask;
        }

        public Task<Payment?> GetByIdAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(SavedPayments.FirstOrDefault(x => x.Id == id));

        public Task<Payment?> GetByChannelReferenceAsync(string channelReference, CancellationToken ct = default) =>
            Task.FromResult(SavedPayments.FirstOrDefault(x => x.ChannelReference == channelReference));
    }

    private class InMemoryIdempotencyRepository : IIdempotencyRepository
    {
        private readonly Dictionary<string, IdempotencyRecord> _store = new();

        public Task<IdempotencyRecord?> FindAsync(string key, string operation, string? paymentId = null, CancellationToken ct = default)
        {
            _store.TryGetValue(key, out var record);
            return Task.FromResult(record);
        }

        public Task AddAsync(IdempotencyRecord record, CancellationToken ct = default)
        {
            _store[record.Key] = record;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(IdempotencyRecord record, CancellationToken ct = default)
        {
            _store[record.Key] = record;
            return Task.CompletedTask;
        }
    }

    private class StubSettlementPort : ISettlementPort
    {
        public int AuthorizeCallCount { get; private set; }
        public Func<string, long, string, string?, SettlementResult>? AuthorizeHandler { get; set; }

        public Task<SettlementResult> AuthorizeAsync(string partyId, long amount, string currency, string? channel = null, CancellationToken ct = default)
        {
            AuthorizeCallCount++;
            if (AuthorizeHandler is not null) return Task.FromResult(AuthorizeHandler(partyId, amount, currency, channel));
            return Task.FromResult(SettlementResult.Success(channel ?? "MOCK", "ref_123", "ch_123"));
        }

        public Task<SettlementResult> AuthorizeAsync(
            string partyId,
            long amount,
            string currency,
            string? channel,
            string? idempotencyKey,
            string? storedPaymentMethodId,
            bool isRecurringMit = false,
            bool storePaymentMethod = false,
            CancellationToken ct = default)
        {
            AuthorizeCallCount++;
            if (AuthorizeHandler is not null) return Task.FromResult(AuthorizeHandler(partyId, amount, currency, channel));
            return Task.FromResult(SettlementResult.Success(channel ?? "MOCK", "ref_123", "ch_123"));
        }

        public Task<SettlementResult> CaptureAsync(string paymentId, string channelReference, long amount, string currency, string? channel = null, CancellationToken ct = default)
            => Task.FromResult(SettlementResult.Success(channel ?? "MOCK", "ref_cap", paymentId));

        public Task<SettlementResult> RefundAsync(string paymentId, string channelReference, long amount, string currency, string? channel = null, CancellationToken ct = default)
            => Task.FromResult(SettlementResult.Success(channel ?? "MOCK", "ref_ref", paymentId));

        public Task<SettlementResult> CancelAsync(string paymentId, string channelReference, string? channel = null, CancellationToken ct = default)
            => Task.FromResult(SettlementResult.Success(channel ?? "MOCK", "ref_cnc", paymentId));
    }

    private class StubInlineRiskEvaluator : IInlineRiskEvaluator
    {
        public int CallCount { get; private set; }
        public Func<string, long, string, InlineRiskEvaluation>? Handler { get; set; }

        public Task<InlineRiskEvaluation> EvaluateAsync(
            string partyId,
            long amount,
            string currency,
            string? settlementChannel = null,
            string? paymentMethodId = null,
            CancellationToken ct = default)
        {
            CallCount++;
            if (Handler is not null)
            {
                return Task.FromResult(Handler(partyId, amount, currency));
            }
            return Task.FromResult(InlineRiskEvaluation.Allow(10, "Default clean"));
        }
    }

    private class StubRiskFlagRepository : IRiskFlagRepository
    {
        public List<RiskFlag> SavedFlags { get; } = [];

        public Task<RiskFlag?> GetByIdAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(SavedFlags.FirstOrDefault(f => f.Id == id));

        public Task<IReadOnlyList<RiskFlag>> GetByPaymentIdAsync(string paymentId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RiskFlag>>(SavedFlags.Where(f => f.PaymentId == paymentId).ToList());

        public Task<(IReadOnlyList<RiskFlag> Items, int TotalCount)> GetOpenFlagsAsync(int page = 1, int pageSize = 50, CancellationToken ct = default) =>
            Task.FromResult<(IReadOnlyList<RiskFlag> Items, int TotalCount)>((SavedFlags, SavedFlags.Count));

        public Task SaveAsync(RiskFlag flag, CancellationToken ct = default)
        {
            SavedFlags.Add(flag);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Handle_WhenRiskEvaluatorBlocks_SkipsSettlementPortAndDeclinesPayment()
    {
        var settlementPort = new StubSettlementPort();
        var paymentRepo = new InMemoryPaymentRepository();
        var idempRepo = new InMemoryIdempotencyRepository();
        var riskFlagRepo = new StubRiskFlagRepository();
        var riskEvaluator = new StubInlineRiskEvaluator
        {
            Handler = (_, _, _) => InlineRiskEvaluation.Block(92, "Known card testing pattern detected")
        };

        var handler = new AuthorizePaymentCommandHandler(
            settlementPort,
            paymentRepo,
            idempRepo,
            paymentMethodRepository: null,
            inlineRiskEvaluator: riskEvaluator,
            riskFlagRepository: riskFlagRepo);

        var command = new AuthorizePaymentCommand(
            "idem-fraud-01",
            "party_fraud_01",
            1000,
            "EUR",
            "MOCK");

        var result = await handler.Handle(command, CancellationToken.None);

        // Settlement port should NEVER be contacted
        settlementPort.AuthorizeCallCount.Should().Be(0);

        // Result and entity should be Declined with FRAUD_BLOCKED reason
        result.State.Should().Be("Declined");
        result.DeclineReason.Should().StartWith("FRAUD_BLOCKED: Known card testing pattern detected");

        paymentRepo.SavedPayments.Should().HaveCount(1);
        paymentRepo.SavedPayments[0].State.Should().Be(PaymentState.Declined);
        paymentRepo.SavedPayments[0].DeclineReason.Should().StartWith("FRAUD_BLOCKED:");

        // Operator RiskFlag must be persisted
        riskFlagRepo.SavedFlags.Should().HaveCount(1);
        var flag = riskFlagRepo.SavedFlags[0];
        flag.PartyId.Should().Be("party_fraud_01");
        flag.PaymentId.Should().Be(result.PaymentId);
        flag.Reason.Should().Contain("Inline risk blocked");
    }

    [Fact]
    public async Task Handle_WhenBlockedPaymentRetried_ReturnsCachedDeclinedDtoWithoutReEvaluation()
    {
        var settlementPort = new StubSettlementPort();
        var paymentRepo = new InMemoryPaymentRepository();
        var idempRepo = new InMemoryIdempotencyRepository();
        var riskFlagRepo = new StubRiskFlagRepository();
        var riskEvaluator = new StubInlineRiskEvaluator
        {
            Handler = (_, _, _) => InlineRiskEvaluation.Block(95, "Blacklisted party")
        };

        var handler = new AuthorizePaymentCommandHandler(
            settlementPort,
            paymentRepo,
            idempRepo,
            paymentMethodRepository: null,
            inlineRiskEvaluator: riskEvaluator,
            riskFlagRepository: riskFlagRepo);

        var command = new AuthorizePaymentCommand(
            "idem-retry-fraud",
            "party_blocked_user",
            500,
            "EUR",
            "MOCK");

        // First attempt
        var first = await handler.Handle(command, CancellationToken.None);
        first.State.Should().Be("Declined");

        // Retry with same idempotency key
        var second = await handler.Handle(command, CancellationToken.None);
        second.State.Should().Be("Declined");
        second.PaymentId.Should().Be(first.PaymentId);

        // Risk evaluator should only have run once for first execution
        riskEvaluator.CallCount.Should().Be(1);
        settlementPort.AuthorizeCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WhenRiskEvaluatorAllows_ProceedsToSettlementPort()
    {
        var settlementPort = new StubSettlementPort();
        var paymentRepo = new InMemoryPaymentRepository();
        var idempRepo = new InMemoryIdempotencyRepository();
        var riskFlagRepo = new StubRiskFlagRepository();
        var riskEvaluator = new StubInlineRiskEvaluator
        {
            Handler = (_, _, _) => InlineRiskEvaluation.Allow(10, "Clean")
        };

        var handler = new AuthorizePaymentCommandHandler(
            settlementPort,
            paymentRepo,
            idempRepo,
            paymentMethodRepository: null,
            inlineRiskEvaluator: riskEvaluator,
            riskFlagRepository: riskFlagRepo);

        var command = new AuthorizePaymentCommand(
            "idem-allow-01",
            "party_clean",
            1000,
            "EUR",
            "MOCK");

        var result = await handler.Handle(command, CancellationToken.None);

        result.State.Should().Be("Authorized");
        settlementPort.AuthorizeCallCount.Should().Be(1);
        riskFlagRepo.SavedFlags.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenRiskEvaluatorReturnsChallenge3Ds_ProceedsToSettlementPortForScaEnforcement()
    {
        var actionDetails = new ThreeDsActionDetails("Redirect", "https://checkoutshopper.adyen.com/3ds2/challenge", "action_token_abc");
        var settlementPort = new StubSettlementPort
        {
            AuthorizeHandler = (_, _, _, ch) => SettlementResult.ActionRequired(ch ?? "MOCK", "ref_3ds_challenge_01", actionDetails)
        };

        var paymentRepo = new InMemoryPaymentRepository();
        var idempRepo = new InMemoryIdempotencyRepository();
        var riskFlagRepo = new StubRiskFlagRepository();
        var riskEvaluator = new StubInlineRiskEvaluator
        {
            Handler = (_, _, _) => InlineRiskEvaluation.Challenge3Ds(72, "Suspicious pattern warrants mandatory 3DS step-up challenge")
        };

        var handler = new AuthorizePaymentCommandHandler(
            settlementPort,
            paymentRepo,
            idempRepo,
            paymentMethodRepository: null,
            inlineRiskEvaluator: riskEvaluator,
            riskFlagRepository: riskFlagRepo);

        var command = new AuthorizePaymentCommand(
            "idem-challenge-3ds-01",
            "party_challenge_needed",
            5000,
            "EUR",
            "MOCK");

        var result = await handler.Handle(command, CancellationToken.None);

        // Verification: settlement is NOT skipped; it is invoked to challenge customer
        settlementPort.AuthorizeCallCount.Should().Be(1);

        // State is RequiresAction with 3DS challenge details
        result.State.Should().Be("RequiresAction");
        result.Action.Should().NotBeNull();
        result.Action!.ActionType.Should().Be("Redirect");
        result.Action.TargetUrl.Should().Be("https://checkoutshopper.adyen.com/3ds2/challenge");

        // Entity is saved as RequiresAction
        paymentRepo.SavedPayments.Should().HaveCount(1);
        paymentRepo.SavedPayments[0].State.Should().Be(PaymentState.RequiresAction);

        // No upfront block flag was created
        riskFlagRepo.SavedFlags.Should().BeEmpty();
    }
}
