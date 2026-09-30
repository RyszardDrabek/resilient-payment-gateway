using FluentAssertions;
using PaymentGateway.Application.Payments;
using PaymentGateway.Domain.Entities;
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

        public Task<SettlementResult> AuthorizeAsync(
            string partyId,
            long amount,
            string currency,
            string? channel = null,
            CancellationToken ct = default)
        {
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

    [Fact]
    public async Task AuthorizePayment_ValidRequest_CreatesAuthorizedPaymentWithResolvedChannel()
    {
        // Arrange
        var settlementPort = new StubSettlementPort { ShouldAuthorize = true, ChannelRef = "ref_123", ActiveChannel = "MOCK" };
        var repository = new InMemoryPaymentRepository();
        var handler = new AuthorizePaymentCommandHandler(settlementPort, repository);

        var command = new AuthorizePaymentCommand("party_1", 1000, "EUR");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.State.Should().Be("Authorized");
        result.SettlementChannel.Should().Be("MOCK");
        result.ChannelReference.Should().Be("ref_123");
        result.DeclineReason.Should().BeNull();
        repository.SavedPayments.Should().HaveCount(1);
        repository.SavedPayments[0].State.Should().Be(PaymentState.Authorized);
    }

    [Fact]
    public async Task AuthorizePayment_DeclinedChannel_CreatesDeclinedPaymentWithReason()
    {
        // Arrange
        var settlementPort = new StubSettlementPort { FailureReason = "Insufficient funds", ActiveChannel = "ADYEN" };
        var repository = new InMemoryPaymentRepository();
        var handler = new AuthorizePaymentCommandHandler(settlementPort, repository);

        var command = new AuthorizePaymentCommand("decline_party", 500, "USD");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.State.Should().Be("Declined");
        result.DeclineReason.Should().Be("Insufficient funds");
        repository.SavedPayments.Should().HaveCount(1);
        repository.SavedPayments[0].State.Should().Be(PaymentState.Declined);
    }

    [Fact]
    public async Task AuthorizePayment_ProviderFailsToAnswer_RecordsDeclinedPayment_AC2()
    {
        // Arrange
        var settlementPort = new StubSettlementPort { ShouldThrow = true };
        var repository = new InMemoryPaymentRepository();
        var handler = new AuthorizePaymentCommandHandler(settlementPort, repository);

        var command = new AuthorizePaymentCommand("party_network_fail", 750, "EUR");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.State.Should().Be("Declined");
        result.DeclineReason.Should().Contain("Settlement channel failed to answer");
        repository.SavedPayments.Should().HaveCount(1);
        repository.SavedPayments[0].State.Should().Be(PaymentState.Declined);
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
