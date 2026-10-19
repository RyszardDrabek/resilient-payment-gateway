using FluentAssertions;
using PaymentGateway.Application.Payments;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Exceptions;
using PaymentGateway.Domain.Ports;
using PaymentGateway.Domain.Services;
using Xunit;

namespace PaymentGateway.UnitTests.Application;

public sealed class SubmitThreeDsResultCommandHandlerTests
{
    private class InMemoryPaymentRepository : IPaymentRepository
    {
        public readonly Dictionary<string, Payment> Payments = new();

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

    private class TestSettlementPort : ISettlementPort
    {
        public bool Succeed { get; set; } = true;
        public string? DeclineReason { get; set; }

        public Task<SettlementResult> AuthorizeAsync(string partyId, long amount, string currency, string? channel = null, CancellationToken ct = default)
            => Task.FromResult(SettlementResult.Success("MOCK", "ref_auth"));

        public Task<SettlementResult> CaptureAsync(string paymentId, string channelReference, long amount, string currency, string? channel = null, CancellationToken ct = default)
            => Task.FromResult(SettlementResult.Success("MOCK", "ref_cap"));

        public Task<SettlementResult> RefundAsync(string paymentId, string channelReference, long amount, string currency, string? channel = null, CancellationToken ct = default)
            => Task.FromResult(SettlementResult.Success("MOCK", "ref_ref"));

        public Task<SettlementResult> CancelAsync(string paymentId, string channelReference, string? channel = null, CancellationToken ct = default)
            => Task.FromResult(SettlementResult.Success("MOCK", "ref_cnc"));

        public Task<SettlementResult> SubmitThreeDsDetailsAsync(
            string paymentId,
            string channelReference,
            string? threeDsResult,
            IReadOnlyDictionary<string, string>? details = null,
            string? channel = null,
            CancellationToken ct = default)
        {
            return Task.FromResult(Succeed
                ? SettlementResult.Success(channel ?? "MOCK", "ref_3ds_approved")
                : SettlementResult.Declined(channel ?? "MOCK", "ref_3ds_declined", DeclineReason ?? "ThreeDsFailed"));
        }
    }

    [Fact]
    public async Task Handle_WhenPaymentRequiresActionAndChallengeSucceeds_TransitionsToAuthorized()
    {
        var repo = new InMemoryPaymentRepository();
        var settlementPort = new TestSettlementPort { Succeed = true };
        var handler = new SubmitThreeDsResultCommandHandler(repo, settlementPort);

        var actionDetails = new ThreeDsActionDetails("threeDS2", "https://bank.example.com/challenge", "token_123");
        var payment = Payment.CreateRequiresAction("merch_test", 10000, "EUR", "MOCK", "ref_init", actionDetails);
        await repo.AddAsync(payment);

        var command = new SubmitThreeDsResultCommand(payment.Id, "challenge_result_token");
        var result = await handler.Handle(command, CancellationToken.None);

        result.State.Should().Be("Authorized");
        result.ChannelReference.Should().Be("ref_3ds_approved");
        result.Action.Should().BeNull();

        var updated = await repo.GetByIdAsync(payment.Id);
        updated!.State.Should().Be(PaymentState.Authorized);
        updated.ThreeDsAction.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenChallengeFails_TransitionsToDeclined()
    {
        var repo = new InMemoryPaymentRepository();
        var settlementPort = new TestSettlementPort { Succeed = false, DeclineReason = "AuthenticationRejected" };
        var handler = new SubmitThreeDsResultCommandHandler(repo, settlementPort);

        var actionDetails = new ThreeDsActionDetails("redirect", "https://bank.example.com", "token_123");
        var payment = Payment.CreateRequiresAction("merch_test", 10000, "EUR", "MOCK", "ref_init", actionDetails);
        await repo.AddAsync(payment);

        var command = new SubmitThreeDsResultCommand(payment.Id, "bad_token");
        var result = await handler.Handle(command, CancellationToken.None);

        result.State.Should().Be("Declined");
        result.DeclineReason.Should().Be("AuthenticationRejected");

        var updated = await repo.GetByIdAsync(payment.Id);
        updated!.State.Should().Be(PaymentState.Declined);
    }

    [Fact]
    public async Task Handle_WhenPaymentNotFound_ThrowsPaymentNotFoundException()
    {
        var repo = new InMemoryPaymentRepository();
        var settlementPort = new TestSettlementPort();
        var handler = new SubmitThreeDsResultCommandHandler(repo, settlementPort);

        var command = new SubmitThreeDsResultCommand("pay_nonexistent", "token");
        var act = async () => await handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<PaymentNotFoundException>();
    }

    [Theory]
    [InlineData(PaymentState.Authorized)]
    [InlineData(PaymentState.Declined)]
    [InlineData(PaymentState.Captured)]
    public async Task Handle_WhenPaymentNotInRequiresAction_ThrowsPaymentInvalidStateException(PaymentState invalidState)
    {
        var repo = new InMemoryPaymentRepository();
        var settlementPort = new TestSettlementPort();
        var handler = new SubmitThreeDsResultCommandHandler(repo, settlementPort);

        var payment = new Payment("pay_1", "merch_test", 5000, "EUR", "MOCK", invalidState);
        await repo.AddAsync(payment);

        var command = new SubmitThreeDsResultCommand(payment.Id, "token");
        var act = async () => await handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<PaymentInvalidStateException>();
    }
}
