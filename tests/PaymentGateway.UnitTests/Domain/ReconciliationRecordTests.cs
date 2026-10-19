using FluentAssertions;
using PaymentGateway.Domain.Entities;
using Xunit;

namespace PaymentGateway.UnitTests.Domain;

public sealed class ReconciliationRecordTests
{
    [Fact]
    public void CreateMatched_InitializesProperly()
    {
        var record = ReconciliationRecord.CreateMatched("pay_123", "ADYEN", "psp_999", 5000);

        record.Id.Should().StartWith("rec_");
        record.PaymentId.Should().Be("pay_123");
        record.Channel.Should().Be("ADYEN");
        record.ChannelReference.Should().Be("psp_999");
        record.GatewayAmount.Should().Be(5000);
        record.AcquirerAmount.Should().Be(5000);
        record.DiscrepancyType.Should().Be(DiscrepancyType.None);
        record.Status.Should().Be(ReconciliationRecordStatus.Matched);
    }

    [Fact]
    public void CreateDiscrepancy_SetsFlaggedStatus()
    {
        var record = ReconciliationRecord.CreateDiscrepancy(
            "pay_123",
            "ADYEN",
            "psp_999",
            gatewayAmount: 5000,
            acquirerAmount: 4800,
            DiscrepancyType.AmountMismatch,
            "Bank deduction unexpected");

        record.DiscrepancyType.Should().Be(DiscrepancyType.AmountMismatch);
        record.Status.Should().Be(ReconciliationRecordStatus.DiscrepancyFlagged);
        record.Notes.Should().Be("Bank deduction unexpected");
    }

    [Fact]
    public void Resolve_UpdatesStatusToResolved()
    {
        var record = ReconciliationRecord.CreateDiscrepancy("pay_123", "ADYEN", "psp_999", 5000, 4800, DiscrepancyType.AmountMismatch);
        record.Resolve("Manually verified exchange fee");

        record.Status.Should().Be(ReconciliationRecordStatus.Resolved);
        record.Notes.Should().Contain("Manually verified exchange fee");
    }
}
