using FluentAssertions;
using PaymentGateway.Application.Payments;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Exceptions;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.UnitTests.Application;

public sealed class ApplyAcquirerOutcomeCommandHandlerTests
{
    private class FakePaymentRepository : IPaymentRepository
    {
        public readonly Dictionary<string, Payment> Payments = [];
        public int UpdateCount { get; private set; }

        public Task AddAsync(Payment payment, CancellationToken ct = default)
        {
            Payments[payment.Id] = payment;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Payment payment, CancellationToken ct = default)
        {
            Payments[payment.Id] = payment;
            UpdateCount++;
            return Task.CompletedTask;
        }

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

        public Task<IReadOnlyList<Payment>> GetUnresolvedAsync(CancellationToken ct = default)
        {
            var list = Payments.Values
                .Where(p => p.State == PaymentState.Pending || p.State == PaymentState.Unknown)
                .ToList();
            return Task.FromResult<IReadOnlyList<Payment>>(list);
        }
    }

    [Fact]
    public async Task Handle_WhenPaymentExists_AppliesOutcomeAndUpdatesRepository()
    {
        // Arrange
        var repo = new FakePaymentRepository();
        var payment = Payment.CreatePending("cust_1", 2000L, "EUR", "ADYEN", "tmp_ref");
        await repo.AddAsync(payment);

        var handler = new ApplyAcquirerOutcomeCommandHandler(repo);
        var command = new ApplyAcquirerOutcomeCommand(payment.Id, PaymentLifecycleOutcome.Authorized, "psp_auth_123");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsApplied.Should().BeTrue();
        result.Payment.Should().NotBeNull();
        result.Payment!.State.Should().Be("Authorized");
        repo.UpdateCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_WhenDuplicateOutcome_DoesNotUpdateRepository_AndReturnsNotApplied()
    {
        // Arrange: payment already authorized
        var repo = new FakePaymentRepository();
        var payment = Payment.Authorize("cust_1", 2000L, "EUR", "ADYEN", "psp_auth_123");
        await repo.AddAsync(payment);

        var handler = new ApplyAcquirerOutcomeCommandHandler(repo);
        var command = new ApplyAcquirerOutcomeCommand(payment.Id, PaymentLifecycleOutcome.Authorized, "psp_auth_123");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsApplied.Should().BeFalse();
        repo.UpdateCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WhenPaymentNotFound_ThrowsPaymentNotFoundException_AndDoesNotInventPayment()
    {
        // Arrange: AC-3 rule: never invent payment
        var repo = new FakePaymentRepository();
        var handler = new ApplyAcquirerOutcomeCommandHandler(repo);
        var command = new ApplyAcquirerOutcomeCommand("pay_non_existent", PaymentLifecycleOutcome.Authorized, "psp_123");

        // Act
        var act = async () => await handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<PaymentNotFoundException>();
        repo.Payments.Should().BeEmpty();
    }
}
