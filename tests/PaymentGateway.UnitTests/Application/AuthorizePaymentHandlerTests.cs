using FluentAssertions;
using PaymentGateway.Application.Payments;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Exceptions;
using PaymentGateway.Domain.Ports;
using Xunit;

namespace PaymentGateway.UnitTests.Application;

public class AuthorizePaymentHandlerTests
{
    private class StubSettlementPort : ISettlementPort
    {
        public bool ShouldAuthorize { get; set; } = true;
        public bool ShouldThrow { get; set; } = false;
        public string ChannelRef { get; set; } = "ref_stub_123";
        public string? FailureReason { get; set; }
        public string ActiveChannel { get; set; } = "MOCK";
        public int AuthorizeCallCount { get; private set; }

        public Task<SettlementResult> AuthorizeAsync(
            string partyId,
            long amount,
            string currency,
            string? channel = null,
            CancellationToken ct = default)
        {
            AuthorizeCallCount++;
            var resolvedChannel = channel ?? ActiveChannel;

            if (ShouldThrow)
            {
                throw new HttpRequestException("Connection timed out");
            }

            if (partyId.StartsWith("decline_") || !ShouldAuthorize)
            {
                return Task.FromResult(new SettlementResult(false, resolvedChannel, ChannelRef, FailureReason ?? "Declined"));
            }

            return Task.FromResult(new SettlementResult(true, resolvedChannel, ChannelRef));
        }
    }

    private class InMemoryPaymentRepository : IPaymentRepository
    {
        public List<Payment> SavedPayments { get; } = [];

        public Task AddAsync(Payment payment, CancellationToken ct = default)
        {
            SavedPayments.Add(payment);
            return Task.CompletedTask;
        }

        public Task<Payment?> GetByIdAsync(string id, CancellationToken ct = default)
        {
            var p = SavedPayments.FirstOrDefault(x => x.Id == id);
            return Task.FromResult(p);
        }
    }

    private class InMemoryIdempotencyRepository : IIdempotencyRepository
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
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task AuthorizePayment_ValidRequest_CreatesAuthorizedPaymentWithResolvedChannel()
    {
        // Arrange
        var settlementPort = new StubSettlementPort { ShouldAuthorize = true, ChannelRef = "ref_123", ActiveChannel = "MOCK" };
        var paymentRepo = new InMemoryPaymentRepository();
        var idemRepo = new InMemoryIdempotencyRepository();
        var handler = new AuthorizePaymentCommandHandler(settlementPort, paymentRepo, idemRepo);

        var command = new AuthorizePaymentCommand("idem_key_1", "party_1", 1000, "EUR");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.State.Should().Be("Authorized");
        result.SettlementChannel.Should().Be("MOCK");
        result.ChannelReference.Should().Be("ref_123");
        result.DeclineReason.Should().BeNull();
        paymentRepo.SavedPayments.Should().HaveCount(1);
        paymentRepo.SavedPayments[0].State.Should().Be(PaymentState.Authorized);
        idemRepo.Records.Should().HaveCount(1);
        idemRepo.Records[0].Status.Should().Be(PaymentGateway.Domain.Enums.IdempotencyStatus.Completed);
    }

    [Fact]
    public async Task AuthorizePayment_MissingIdempotencyKey_ThrowsIdempotencyKeyMissingException()
    {
        // Arrange
        var settlementPort = new StubSettlementPort();
        var paymentRepo = new InMemoryPaymentRepository();
        var idemRepo = new InMemoryIdempotencyRepository();
        var handler = new AuthorizePaymentCommandHandler(settlementPort, paymentRepo, idemRepo);

        var command = new AuthorizePaymentCommand("", "party_1", 1000, "EUR");

        // Act
        var act = () => handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<IdempotencyKeyMissingException>();
    }

    [Fact]
    public async Task AuthorizePayment_DuplicateKey_SamePayload_ReplaysOriginalOutcomeWithoutSettling()
    {
        // Arrange
        var settlementPort = new StubSettlementPort { ChannelRef = "ref_original_1" };
        var paymentRepo = new InMemoryPaymentRepository();
        var idemRepo = new InMemoryIdempotencyRepository();
        var handler = new AuthorizePaymentCommandHandler(settlementPort, paymentRepo, idemRepo);

        var command1 = new AuthorizePaymentCommand("idem_replay_key", "party_1", 1500, "EUR");
        var command2 = new AuthorizePaymentCommand("idem_replay_key", "party_1", 1500, "EUR");

        // Act
        var result1 = await handler.Handle(command1, CancellationToken.None);
        var result2 = await handler.Handle(command2, CancellationToken.None);

        // Assert
        settlementPort.AuthorizeCallCount.Should().Be(1);
        paymentRepo.SavedPayments.Should().HaveCount(1);
        result2.Should().NotBeNull();
        result2.PaymentId.Should().Be(result1.PaymentId);
        result2.ChannelReference.Should().Be("ref_original_1");
    }

    [Fact]
    public async Task AuthorizePayment_DuplicateKey_DifferentPayload_ThrowsConflictException()
    {
        // Arrange
        var settlementPort = new StubSettlementPort();
        var paymentRepo = new InMemoryPaymentRepository();
        var idemRepo = new InMemoryIdempotencyRepository();
        var handler = new AuthorizePaymentCommandHandler(settlementPort, paymentRepo, idemRepo);

        var command1 = new AuthorizePaymentCommand("idem_conflict_key", "party_1", 1500, "EUR");
        var command2 = new AuthorizePaymentCommand("idem_conflict_key", "party_1", 2000, "EUR"); // Different amount

        // Act
        await handler.Handle(command1, CancellationToken.None);
        var act = () => handler.Handle(command2, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<IdempotencyConflictException>();
        paymentRepo.SavedPayments.Should().HaveCount(1);
    }

    [Fact]
    public async Task AuthorizePayment_DuplicateKey_InFlight_ThrowsInFlightException()
    {
        // Arrange
        var settlementPort = new StubSettlementPort();
        var paymentRepo = new InMemoryPaymentRepository();
        var idemRepo = new InMemoryIdempotencyRepository();

        var inFlightRecord = IdempotencyRecord.CreateInFlight(
            "idem_inflight_key",
            "Authorize",
            PaymentGateway.Domain.Services.IdempotencyPayloadHasher.ComputeHash("Authorize", null, "party_1", 1000, "EUR"));
        idemRepo.Records.Add(inFlightRecord);

        var handler = new AuthorizePaymentCommandHandler(settlementPort, paymentRepo, idemRepo);
        var command = new AuthorizePaymentCommand("idem_inflight_key", "party_1", 1000, "EUR");

        // Act
        var act = () => handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<IdempotencyInFlightException>();
        settlementPort.AuthorizeCallCount.Should().Be(0);
    }

    [Fact]
    public async Task AuthorizePayment_DeclinedChannel_CreatesDeclinedPaymentWithReason()
    {
        // Arrange
        var settlementPort = new StubSettlementPort { FailureReason = "Insufficient funds", ActiveChannel = "ADYEN" };
        var paymentRepo = new InMemoryPaymentRepository();
        var idemRepo = new InMemoryIdempotencyRepository();
        var handler = new AuthorizePaymentCommandHandler(settlementPort, paymentRepo, idemRepo);

        var command = new AuthorizePaymentCommand("idem_decline_1", "decline_party", 500, "USD");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.State.Should().Be("Declined");
        result.DeclineReason.Should().Be("Insufficient funds");
        paymentRepo.SavedPayments.Should().HaveCount(1);
        paymentRepo.SavedPayments[0].State.Should().Be(PaymentState.Declined);
    }

    [Fact]
    public async Task AuthorizePayment_ProviderFailsToAnswer_RecordsDeclinedPayment_AC2()
    {
        // Arrange
        var settlementPort = new StubSettlementPort { ShouldThrow = true };
        var paymentRepo = new InMemoryPaymentRepository();
        var idemRepo = new InMemoryIdempotencyRepository();
        var handler = new AuthorizePaymentCommandHandler(settlementPort, paymentRepo, idemRepo);

        var command = new AuthorizePaymentCommand("idem_fail_1", "party_network_fail", 750, "EUR");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.State.Should().Be("Declined");
        result.DeclineReason.Should().Contain("Settlement channel failed to answer");
        paymentRepo.SavedPayments.Should().HaveCount(1);
        paymentRepo.SavedPayments[0].State.Should().Be(PaymentState.Declined);
    }

    [Fact]
    public async Task GetPayment_ExistingId_ReturnsPaymentDetailsWithDeclineReason()
    {
        // Arrange
        var repository = new InMemoryPaymentRepository();
        var payment = Payment.Decline("party_2", 1500, "EUR", "MOCK", "ref_99", "Limit exceeded");
        await repository.AddAsync(payment);

        var queryHandler = new GetPaymentByIdQueryHandler(repository);

        // Act
        var result = await queryHandler.Handle(new GetPaymentByIdQuery(payment.Id), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.PaymentId.Should().Be(payment.Id);
        result.PartyId.Should().Be("party_2");
        result.State.Should().Be("Declined");
        result.DeclineReason.Should().Be("Limit exceeded");
    }

    [Fact]
    public async Task GetPayment_NonExistingId_ReturnsNull()
    {
        // Arrange
        var repository = new InMemoryPaymentRepository();
        var queryHandler = new GetPaymentByIdQueryHandler(repository);

        // Act
        var result = await queryHandler.Handle(new GetPaymentByIdQuery("non_existent_id"), CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }
}
