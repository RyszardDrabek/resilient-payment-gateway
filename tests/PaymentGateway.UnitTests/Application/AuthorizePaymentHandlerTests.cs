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
        public string ChannelRef { get; set; } = "ref_stub_123";
        public string? FailureReason { get; set; }

        public Task<SettlementResult> AuthorizeAsync(
            string partyId,
            long amount,
            string currency,
            string channel,
            CancellationToken ct = default)
        {
            if (partyId.StartsWith("decline_") || !ShouldAuthorize)
            {
                return Task.FromResult(new SettlementResult(false, ChannelRef, FailureReason ?? "Declined"));
            }

            return Task.FromResult(new SettlementResult(true, ChannelRef));
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
    public async Task AuthorizePayment_ValidRequest_CreatesAuthorizedPayment()
    {
        // Arrange
        var settlementPort = new StubSettlementPort { ShouldAuthorize = true, ChannelRef = "ref_123" };
        var repository = new InMemoryPaymentRepository();
        var handler = new AuthorizePaymentCommandHandler(settlementPort, repository);

        var command = new AuthorizePaymentCommand("party_1", 1000, "EUR", "MOCK");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.State.Should().Be("Authorized");
        result.ChannelReference.Should().Be("ref_123");
        repository.SavedPayments.Should().HaveCount(1);
        repository.SavedPayments[0].State.Should().Be(PaymentState.Authorized);
    }

    [Fact]
    public async Task AuthorizePayment_DeclinedChannel_CreatesDeclinedPayment()
    {
        // Arrange
        var settlementPort = new StubSettlementPort();
        var repository = new InMemoryPaymentRepository();
        var handler = new AuthorizePaymentCommandHandler(settlementPort, repository);

        var command = new AuthorizePaymentCommand("decline_party", 500, "USD", "ADYEN");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.State.Should().Be("Declined");
        repository.SavedPayments.Should().HaveCount(1);
        repository.SavedPayments[0].State.Should().Be(PaymentState.Declined);
    }

    [Fact]
    public async Task GetPayment_ExistingId_ReturnsPaymentDetails()
    {
        // Arrange
        var repository = new InMemoryPaymentRepository();
        var payment = Payment.Authorize("party_2", 1500, "EUR", "MOCK", "ref_99");
        await repository.AddAsync(payment);

        var queryHandler = new GetPaymentByIdQueryHandler(repository);

        // Act
        var result = await queryHandler.Handle(new GetPaymentByIdQuery(payment.Id), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.PaymentId.Should().Be(payment.Id);
        result.PartyId.Should().Be("party_2");
    }
}
