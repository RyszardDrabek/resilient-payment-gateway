using FluentAssertions;
using PaymentGateway.Application.Payments;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Exceptions;
using PaymentGateway.Domain.Ports;
using Xunit;

namespace PaymentGateway.UnitTests.Application;

public sealed class PaymentTransitionsCommandHandlerTests
{
    private class TestSettlementPort : ISettlementPort
    {
        public bool Succeed { get; set; } = true;
        public string Channel { get; set; } = "MOCK";
        public string ChannelReference { get; set; } = "ref_test_123";
        public string? DeclineReason { get; set; }
        public bool ThrowException { get; set; }
        public int CallCount { get; private set; }

        public Task<SettlementResult> AuthorizeAsync(string partyId, long amount, string currency, string? channel = null, CancellationToken ct = default)
            => Task.FromResult(SettlementResult.Success(channel ?? Channel, ChannelReference));

        public Task<SettlementResult> CaptureAsync(string paymentId, string channelReference, long amount, string currency, string? channel = null, CancellationToken ct = default)
        {
            CallCount++;
            if (ThrowException) throw new HttpRequestException("Network failure");
            return Task.FromResult(Succeed
                ? SettlementResult.Success(channel ?? Channel, "ref_cap_test", paymentId)
                : SettlementResult.Declined(channel ?? Channel, "ref_cap_declined", DeclineReason ?? "Capture declined"));
        }

        public Task<SettlementResult> CancelAsync(string paymentId, string channelReference, string? channel = null, CancellationToken ct = default)
        {
            CallCount++;
            if (ThrowException) throw new HttpRequestException("Network failure");
            return Task.FromResult(Succeed
                ? SettlementResult.Success(channel ?? Channel, "ref_cnc_test", paymentId)
                : SettlementResult.Declined(channel ?? Channel, "ref_cnc_declined", DeclineReason ?? "Cancel declined"));
        }

        public Task<SettlementResult> RefundAsync(string paymentId, string channelReference, long amount, string currency, string? channel = null, CancellationToken ct = default)
        {
            CallCount++;
            if (ThrowException) throw new HttpRequestException("Network failure");
            return Task.FromResult(Succeed
                ? SettlementResult.Success(channel ?? Channel, "ref_ref_test", paymentId)
                : SettlementResult.Declined(channel ?? Channel, "ref_ref_declined", DeclineReason ?? "Refund declined"));
        }
    }

    private class TestPaymentRepository : IPaymentRepository
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

        public Task<Payment?> GetByIdAsync(string id, CancellationToken ct = default)
        {
            var p = SavedPayments.FirstOrDefault(x => x.Id == id);
            return Task.FromResult(p);
        }
    }

    private class TestIdempotencyRepository : IIdempotencyRepository
    {
        public List<IdempotencyRecord> Records { get; } = [];

        public Task<IdempotencyRecord?> FindAsync(string key, string commandType, string? paymentId, CancellationToken ct)
        {
            var r = Records.FirstOrDefault(x => x.Key == key && x.CommandType == commandType && x.PaymentId == paymentId);
            return Task.FromResult(r);
        }

        public Task AddAsync(IdempotencyRecord record, CancellationToken ct)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(IdempotencyRecord record, CancellationToken ct)
        {
            var idx = Records.FindIndex(x => x.Id == record.Id);
            if (idx >= 0) Records[idx] = record;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Capture_WhenPaymentNotFound_ThrowsPaymentNotFoundException()
    {
        var repo = new TestPaymentRepository();
        var port = new TestSettlementPort();
        var idem = new TestIdempotencyRepository();
        var handler = new CapturePaymentCommandHandler(repo, port, idem);

        var act = () => handler.Handle(new CapturePaymentCommand("pay_not_found", "idem_key_1"), CancellationToken.None);

        await act.Should().ThrowAsync<PaymentNotFoundException>();
    }

    [Fact]
    public async Task Capture_WhenChannelSucceeds_UpdatesPaymentToCaptured()
    {
        var repo = new TestPaymentRepository();
        var port = new TestSettlementPort();
        var idem = new TestIdempotencyRepository();
        var handler = new CapturePaymentCommandHandler(repo, port, idem);

        var payment = Payment.Authorize("party_1", 3000, "EUR", "MOCK", "ref_auth_1");
        await repo.AddAsync(payment);

        var result = await handler.Handle(new CapturePaymentCommand(payment.Id, "idem_key_1"), CancellationToken.None);

        result.State.Should().Be("Captured");
        result.ChannelReference.Should().Be("ref_cap_test");

        var updated = await repo.GetByIdAsync(payment.Id);
        updated!.State.Should().Be(PaymentState.Captured);
        updated.ChannelReference.Should().Be("ref_cap_test");
    }

    [Fact]
    public async Task Capture_WhenChannelFails_LeavesPaymentInPriorState_AndThrowsPaymentOperationFailedException()
    {
        var repo = new TestPaymentRepository();
        var port = new TestSettlementPort { Succeed = false, DeclineReason = "Acquirer rejected" };
        var idem = new TestIdempotencyRepository();
        var handler = new CapturePaymentCommandHandler(repo, port, idem);

        var payment = Payment.Authorize("party_1", 3000, "EUR", "MOCK", "ref_auth_1");
        await repo.AddAsync(payment);

        var act = () => handler.Handle(new CapturePaymentCommand(payment.Id, "idem_key_1"), CancellationToken.None);

        await act.Should().ThrowAsync<PaymentOperationFailedException>();

        var unchanged = await repo.GetByIdAsync(payment.Id);
        unchanged!.State.Should().Be(PaymentState.Authorized);
    }

    [Fact]
    public async Task Cancel_WhenChannelSucceeds_UpdatesPaymentToCancelled()
    {
        var repo = new TestPaymentRepository();
        var port = new TestSettlementPort();
        var idem = new TestIdempotencyRepository();
        var handler = new CancelPaymentCommandHandler(repo, port, idem);

        var payment = Payment.Authorize("party_1", 3000, "EUR", "MOCK", "ref_auth_1");
        await repo.AddAsync(payment);

        var result = await handler.Handle(new CancelPaymentCommand(payment.Id, "idem_key_1"), CancellationToken.None);

        result.State.Should().Be("Cancelled");
        result.ChannelReference.Should().Be("ref_cnc_test");

        var updated = await repo.GetByIdAsync(payment.Id);
        updated!.State.Should().Be(PaymentState.Cancelled);
    }

    [Fact]
    public async Task Cancel_WhenChannelFails_LeavesPaymentInPriorState_AndThrowsPaymentOperationFailedException()
    {
        var repo = new TestPaymentRepository();
        var port = new TestSettlementPort { ThrowException = true };
        var idem = new TestIdempotencyRepository();
        var handler = new CancelPaymentCommandHandler(repo, port, idem);

        var payment = Payment.Authorize("party_1", 3000, "EUR", "MOCK", "ref_auth_1");
        await repo.AddAsync(payment);

        var act = () => handler.Handle(new CancelPaymentCommand(payment.Id, "idem_key_1"), CancellationToken.None);

        await act.Should().ThrowAsync<PaymentOperationFailedException>();

        var unchanged = await repo.GetByIdAsync(payment.Id);
        unchanged!.State.Should().Be(PaymentState.Authorized);
    }

    [Fact]
    public async Task Refund_WhenChannelSucceeds_UpdatesPaymentToRefunded()
    {
        var repo = new TestPaymentRepository();
        var port = new TestSettlementPort();
        var idem = new TestIdempotencyRepository();
        var handler = new RefundPaymentCommandHandler(repo, port, idem);

        var payment = Payment.Authorize("party_1", 3000, "EUR", "MOCK", "ref_auth_1");
        payment.Capture("ref_cap_1");
        await repo.AddAsync(payment);

        var result = await handler.Handle(new RefundPaymentCommand(payment.Id, "idem_key_1"), CancellationToken.None);

        result.State.Should().Be("Refunded");
        result.ChannelReference.Should().Be("ref_ref_test");

        var updated = await repo.GetByIdAsync(payment.Id);
        updated!.State.Should().Be(PaymentState.Refunded);
    }

    [Fact]
    public async Task Refund_WhenChannelFails_LeavesPaymentInPriorState_AndThrowsPaymentOperationFailedException()
    {
        var repo = new TestPaymentRepository();
        var port = new TestSettlementPort { Succeed = false, DeclineReason = "Refund declined" };
        var idem = new TestIdempotencyRepository();
        var handler = new RefundPaymentCommandHandler(repo, port, idem);

        var payment = Payment.Authorize("party_1", 3000, "EUR", "MOCK", "ref_auth_1");
        payment.Capture("ref_cap_1");
        await repo.AddAsync(payment);

        var act = () => handler.Handle(new RefundPaymentCommand(payment.Id, "idem_key_1"), CancellationToken.None);

        await act.Should().ThrowAsync<PaymentOperationFailedException>();

        var unchanged = await repo.GetByIdAsync(payment.Id);
        unchanged!.State.Should().Be(PaymentState.Captured);
    }

    [Fact]
    public async Task Command_ReplaySameKey_ReturnsExistingResponseWithoutCallingChannel()
    {
        var repo = new TestPaymentRepository();
        var port = new TestSettlementPort();
        var idem = new TestIdempotencyRepository();
        var handler = new CapturePaymentCommandHandler(repo, port, idem);

        var payment = Payment.Authorize("party_1", 3000, "EUR", "MOCK", "ref_auth_1");
        payment.Capture("ref_cap_1");
        await repo.AddAsync(payment);

        var payloadHash = PaymentGateway.Domain.Services.IdempotencyPayloadHasher.ComputeHash(
            "Capture", payment.Id, "party_1", 3000, "EUR", "MOCK");
        var completedRecord = IdempotencyRecord.CreateInFlight("idem_replay", "Capture", payloadHash, payment.Id);
        var cachedDto = new PaymentDto(payment.Id, "party_1", 3000, "EUR", "MOCK", "Captured", "ref_cap_1", null, DateTimeOffset.UtcNow);
        completedRecord.Complete(200, System.Text.Json.JsonSerializer.Serialize(cachedDto), payment.Id);
        await idem.AddAsync(completedRecord, CancellationToken.None);

        var result = await handler.Handle(new CapturePaymentCommand(payment.Id, "idem_replay"), CancellationToken.None);

        result.State.Should().Be("Captured");
        port.CallCount.Should().Be(0);
    }
}
