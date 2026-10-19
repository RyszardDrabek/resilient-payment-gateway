using FluentAssertions;
using PaymentGateway.Domain.Entities;
using Xunit;

namespace PaymentGateway.UnitTests.Domain;

public sealed class MerchantPayoutBatchTests
{
    [Fact]
    public void AddItem_AccumulatesTotalsProperly()
    {
        var start = DateTimeOffset.UtcNow.AddDays(-1);
        var end = DateTimeOffset.UtcNow;
        var batch = new MerchantPayoutBatch("payout_1", "merch_acme", start, end, "EUR");

        var item1 = new PayoutItem("pi_1", "payout_1", "pay_1", 10000, 150, "EUR");
        var item2 = new PayoutItem("pi_2", "payout_1", "pay_2", 5000, 75, "EUR");

        batch.AddItem(item1);
        batch.AddItem(item2);

        batch.GrossAmount.Should().Be(15000);
        batch.FeeAmount.Should().Be(225);
        batch.NetAmount.Should().Be(14775);
        batch.Items.Should().HaveCount(2);
        batch.Status.Should().Be(PayoutStatus.Pending);
    }

    [Fact]
    public void StateTransitions_FollowAllowedLifecycle()
    {
        var start = DateTimeOffset.UtcNow.AddDays(-1);
        var end = DateTimeOffset.UtcNow;
        var batch = new MerchantPayoutBatch("payout_1", "merch_acme", start, end, "EUR");

        batch.MarkProcessing();
        batch.Status.Should().Be(PayoutStatus.Processing);

        var executedAt = DateTimeOffset.UtcNow;
        batch.MarkCompleted(executedAt);
        batch.Status.Should().Be(PayoutStatus.Completed);
        batch.ExecutedAt.Should().Be(executedAt);
    }
}
