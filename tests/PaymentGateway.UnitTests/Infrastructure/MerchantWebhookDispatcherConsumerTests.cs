using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PaymentGateway.Application.Events;
using PaymentGateway.Application.Webhooks;
using PaymentGateway.Infrastructure.Consumers;
using Xunit;

namespace PaymentGateway.UnitTests.Infrastructure;

public sealed class MerchantWebhookDispatcherConsumerTests
{
    private sealed class StubMerchantWebhookDispatcher : IMerchantWebhookDispatcher
    {
        public int CallCount { get; private set; }
        public PaymentLifecycleEvent? LastEvent { get; private set; }
        public MerchantWebhookDispatchResult StubResult { get; set; } = new(true, "https://merchant.example/hook", "{}", 1);

        public Task<MerchantWebhookDispatchResult> DispatchAsync(PaymentLifecycleEvent lifecycleEvent, CancellationToken ct = default)
        {
            CallCount++;
            LastEvent = lifecycleEvent;
            return Task.FromResult(StubResult);
        }
    }

    [Theory]
    [InlineData("Captured")]
    [InlineData("captured")]
    [InlineData("PartiallyCaptured")]
    [InlineData("Refunded")]
    [InlineData("PartiallyRefunded")]
    [InlineData("Cancelled")]
    [InlineData("Declined")]
    public void IsTerminalState_TerminalOutcomes_ReturnsTrue(string outcome)
    {
        MerchantWebhookDispatcherConsumer.IsTerminalState(outcome).Should().BeTrue();
    }

    [Theory]
    [InlineData("Authorized")]
    [InlineData("Pending")]
    [InlineData("Unknown")]
    [InlineData("")]
    [InlineData(null)]
    public void IsTerminalState_NonTerminalOutcomes_ReturnsFalse(string? outcome)
    {
        MerchantWebhookDispatcherConsumer.IsTerminalState(outcome).Should().BeFalse();
    }

    [Fact]
    public async Task ProcessEventAsync_NonTerminalEvent_DoesNotCallDispatcher()
    {
        var dispatcher = new StubMerchantWebhookDispatcher();
        var consumer = new MerchantWebhookDispatcherConsumer(dispatcher, NullLogger<MerchantWebhookDispatcherConsumer>.Instance);

        var evt = new PaymentLifecycleEvent(
            EventId: "evt_1",
            PaymentId: "pay_1",
            PartyId: "party_1",
            Outcome: "authorized",
            Amount: 1000,
            Currency: "EUR",
            Version: 1,
            OccurredAt: DateTimeOffset.UtcNow);

        var dispatched = await consumer.ProcessEventAsync(evt);

        dispatched.Should().BeFalse();
        dispatcher.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ProcessEventAsync_TerminalEvent_CallsDispatcher()
    {
        var dispatcher = new StubMerchantWebhookDispatcher();
        var consumer = new MerchantWebhookDispatcherConsumer(dispatcher, NullLogger<MerchantWebhookDispatcherConsumer>.Instance);

        var evt = new PaymentLifecycleEvent(
            EventId: "evt_2",
            PaymentId: "pay_2",
            PartyId: "party_2",
            Outcome: "captured",
            Amount: 2000,
            Currency: "EUR",
            Version: 2,
            OccurredAt: DateTimeOffset.UtcNow);

        var dispatched = await consumer.ProcessEventAsync(evt);

        dispatched.Should().BeTrue();
        dispatcher.CallCount.Should().Be(1);
        dispatcher.LastEvent.Should().NotBeNull();
        dispatcher.LastEvent!.PaymentId.Should().Be("pay_2");
    }
}
