using FluentAssertions;
using PaymentGateway.Application.Payouts;
using PaymentGateway.Application.Reconciliation;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Ports;
using Xunit;

namespace PaymentGateway.UnitTests.Application;

public sealed class ReconciliationAndPayoutHandlerTests
{
    private sealed class FakeFeeScheduleRepository : IFeeScheduleRepository
    {
        public FeeSchedule? Schedule { get; set; }
        public Task<FeeSchedule?> GetActiveFeeScheduleAsync(string merchantId, string currency, DateTimeOffset asOf, CancellationToken ct = default) =>
            Task.FromResult(Schedule);
        public Task SaveAsync(FeeSchedule schedule, CancellationToken ct = default)
        {
            Schedule = schedule;
            return Task.CompletedTask;
        }
    }

    private sealed class FakePaymentRepository : IPaymentRepository
    {
        public List<Payment> CapturedPayments { get; } = [];
        public Task AddAsync(Payment payment, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Payment payment, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Payment?> GetByIdAsync(string id, CancellationToken ct = default) => Task.FromResult<Payment?>(null);
        public Task<Payment?> GetByChannelReferenceAsync(string channelReference, CancellationToken ct = default) => Task.FromResult<Payment?>(null);
        public Task<IReadOnlyList<Payment>> GetCapturedPaymentsAsync(DateTimeOffset from, DateTimeOffset to, string? partyId = null, string? currency = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Payment>>(CapturedPayments);
    }

    private sealed class FakeReconciliationRepository : IReconciliationRepository
    {
        public List<ReconciliationRecord> SavedRecords { get; } = [];
        public Task<ReconciliationRecord?> GetByIdAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(SavedRecords.FirstOrDefault(r => r.Id == id));
        public Task SaveAsync(ReconciliationRecord record, CancellationToken ct = default)
        {
            SavedRecords.RemoveAll(r => r.Id == record.Id);
            SavedRecords.Add(record);
            return Task.CompletedTask;
        }
        public Task SaveBatchAsync(IEnumerable<ReconciliationRecord> records, CancellationToken ct = default)
        {
            SavedRecords.AddRange(records);
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<ReconciliationRecord>> ListDiscrepanciesAsync(int skip = 0, int take = 50, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ReconciliationRecord>>(SavedRecords.Where(r => r.Status == ReconciliationRecordStatus.DiscrepancyFlagged).Skip(skip).Take(take).ToList());
        public Task<ReconciliationSummary> GetSummaryAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
        {
            var total = SavedRecords.Count;
            var matched = SavedRecords.Where(r => r.Status == ReconciliationRecordStatus.Matched).ToList();
            var discrepancies = SavedRecords.Where(r => r.Status == ReconciliationRecordStatus.DiscrepancyFlagged).ToList();
            return Task.FromResult(new ReconciliationSummary(
                total,
                matched.Count,
                discrepancies.Count,
                matched.Sum(r => r.GatewayAmount),
                discrepancies.Sum(r => Math.Abs(r.GatewayAmount - r.AcquirerAmount))));
        }
    }

    private sealed class FakePayoutRepository : IPayoutRepository
    {
        public List<MerchantPayoutBatch> Batches { get; } = [];
        public Task<MerchantPayoutBatch?> GetByIdAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(Batches.FirstOrDefault(b => b.Id == id));
        public Task<IReadOnlyList<MerchantPayoutBatch>> ListByMerchantIdAsync(string merchantId, int skip = 0, int take = 50, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MerchantPayoutBatch>>(Batches.Where(b => b.MerchantId == merchantId).Skip(skip).Take(take).ToList());
        public Task SaveAsync(MerchantPayoutBatch batch, CancellationToken ct = default)
        {
            Batches.RemoveAll(b => b.Id == batch.Id);
            Batches.Add(batch);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task FeeCalculator_WhenCustomScheduleExists_UsesScheduleRates()
    {
        var feeRepo = new FakeFeeScheduleRepository
        {
            Schedule = new FeeSchedule("fee_1", "merch_1", "EUR", fixedFeeMinorUnits: 25, percentageFeeBps: 200, DateTimeOffset.UtcNow.AddDays(-10))
        };

        var calculator = new FeeCalculator(feeRepo);
        var fee = await calculator.CalculateFeeAsync("merch_1", "EUR", 10000, DateTimeOffset.UtcNow);

        fee.Should().Be(225);
    }

    [Fact]
    public async Task FeeCalculator_WhenNoScheduleExists_UsesDefaultRates()
    {
        var feeRepo = new FakeFeeScheduleRepository();
        var calculator = new FeeCalculator(feeRepo);

        var fee = await calculator.CalculateFeeAsync("merch_1", "EUR", 10000, DateTimeOffset.UtcNow);
        fee.Should().Be(130);
    }

    [Fact]
    public async Task RunReconciliationHandler_MatchesValidPayments_AndFlagsMissingReferences()
    {
        var paymentRepo = new FakePaymentRepository();
        var reconRepo = new FakeReconciliationRepository();

        var p1 = new Payment("pay_1", "merch_1", 5000, "EUR", "ADYEN", PaymentState.Captured, channelReference: "psp_123", capturedAmount: 5000);
        paymentRepo.CapturedPayments.Add(p1);

        var p2 = new Payment("pay_2", "merch_1", 3000, "EUR", "ADYEN", PaymentState.Captured, channelReference: null, capturedAmount: 3000);
        paymentRepo.CapturedPayments.Add(p2);

        var handler = new RunReconciliationCommandHandler(paymentRepo, reconRepo);
        var result = await handler.Handle(new RunReconciliationCommand(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow), CancellationToken.None);

        result.TotalProcessed.Should().Be(2);
        result.MatchedCount.Should().Be(1);
        result.DiscrepancyCount.Should().Be(1);
        reconRepo.SavedRecords.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetReconciliationSummaryHandler_ComputesPercentagesProperly()
    {
        var reconRepo = new FakeReconciliationRepository();
        for (int i = 0; i < 95; i++)
        {
            reconRepo.SavedRecords.Add(ReconciliationRecord.CreateMatched($"pay_{i}", "ADYEN", $"psp_{i}", 5000));
        }
        for (int i = 0; i < 5; i++)
        {
            reconRepo.SavedRecords.Add(ReconciliationRecord.CreateDiscrepancy($"pay_d_{i}", "ADYEN", "UNKNOWN", 5000, 0, DiscrepancyType.MissingInAcquirer));
        }

        var handler = new GetReconciliationSummaryQueryHandler(reconRepo);
        var result = await handler.Handle(new GetReconciliationSummaryQuery(), CancellationToken.None);

        result.TotalRecords.Should().Be(100);
        result.MatchedCount.Should().Be(95);
        result.DiscrepancyCount.Should().Be(5);
        result.MatchRatePercentage.Should().Be(95.0);
    }

    [Fact]
    public async Task ResolveDiscrepancyHandler_ResolvesExistingRecord()
    {
        var reconRepo = new FakeReconciliationRepository();
        var record = ReconciliationRecord.CreateDiscrepancy("pay_1", "ADYEN", "UNKNOWN", 5000, 0, DiscrepancyType.MissingInAcquirer);
        await reconRepo.SaveAsync(record);

        var handler = new ResolveDiscrepancyCommandHandler(reconRepo);
        var success = await handler.Handle(new ResolveDiscrepancyCommand(record.Id, "Manual settlement voucher verified"), CancellationToken.None);

        success.Should().BeTrue();
        var updated = await reconRepo.GetByIdAsync(record.Id);
        updated!.Status.Should().Be(ReconciliationRecordStatus.Resolved);
        updated.Notes.Should().Contain("Manual settlement voucher verified");
    }

    [Fact]
    public async Task GeneratePayoutBatchHandler_AggregatesCapturedPaymentsAndCalculatesFees()
    {
        var paymentRepo = new FakePaymentRepository();
        var payoutRepo = new FakePayoutRepository();
        var feeRepo = new FakeFeeScheduleRepository();
        var feeCalc = new FeeCalculator(feeRepo);

        var p1 = new Payment("pay_1", "merch_1", 10000, "EUR", "ADYEN", PaymentState.Captured, channelReference: "psp_1", capturedAmount: 10000);
        paymentRepo.CapturedPayments.Add(p1);

        var p2 = new Payment("pay_2", "merch_1", 20000, "EUR", "ADYEN", PaymentState.Captured, channelReference: "psp_2", capturedAmount: 20000);
        paymentRepo.CapturedPayments.Add(p2);

        var handler = new GeneratePayoutBatchCommandHandler(paymentRepo, payoutRepo, feeCalc);
        var result = await handler.Handle(new GeneratePayoutBatchCommand(
            "merch_1",
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow,
            "EUR"), CancellationToken.None);

        // For p1 (10000): 10 + 120 = 130
        // For p2 (20000): 10 + 240 = 250
        // Total fees = 380, Gross = 30000, Net = 29620
        result.GrossAmount.Should().Be(30000);
        result.FeeAmount.Should().Be(380);
        result.NetAmount.Should().Be(29620);
        result.Status.Should().Be(PayoutStatus.Completed.ToString());
        result.Items.Should().HaveCount(2);
        payoutRepo.Batches.Should().ContainSingle();
    }

    [Fact]
    public async Task GetPayoutBatchCsvQueryHandler_FormatsValidCsvFile()
    {
        var payoutRepo = new FakePayoutRepository();
        var batch = new MerchantPayoutBatch("payout_1", "merch_1", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, "EUR");
        batch.AddItem(new PayoutItem("pi_1", batch.Id, "pay_1", 10000, 130, "EUR", DateTimeOffset.UtcNow));
        batch.MarkCompleted();
        await payoutRepo.SaveAsync(batch);

        var handler = new GetPayoutBatchCsvQueryHandler(payoutRepo);
        var result = await handler.Handle(new GetPayoutBatchCsvQuery("payout_1"), CancellationToken.None);

        result.Should().NotBeNull();
        result!.FileName.Should().Contain("payout_merch_1_");
        result.CsvContent.Should().Contain("Payout Item ID,Payment ID,Captured At (UTC),Gross Amount,Fee Amount,Net Amount,Currency");
        result.CsvContent.Should().Contain("pi_1,pay_1,");
        result.CsvContent.Should().Contain("# Total Gross: 10000 EUR");
        result.CsvContent.Should().Contain("# Total Fees: 130 EUR");
        result.CsvContent.Should().Contain("# Total Net: 9870 EUR");
    }
}
