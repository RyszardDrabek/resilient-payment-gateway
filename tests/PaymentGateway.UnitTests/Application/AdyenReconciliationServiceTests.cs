using FluentAssertions;
using MediatR;
using PaymentGateway.Application.Adyen;
using PaymentGateway.Application.Payments;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Ports;

namespace PaymentGateway.UnitTests.Application;

public sealed class AdyenReconciliationServiceTests
{
    private class FakeNotificationRepository : IAdyenNotificationRepository
    {
        public readonly Dictionary<Guid, AdyenNotification> Notifications = [];

        public Task<bool> ExistsAsync(string pspReference, string eventCode, CancellationToken ct) =>
            Task.FromResult(Notifications.Values.Any(n => n.PspReference == pspReference && n.EventCode == eventCode));

        public Task AddAsync(AdyenNotification notification, CancellationToken ct)
        {
            Notifications[notification.Id] = notification;
            return Task.CompletedTask;
        }

        public Task<AdyenNotification?> GetByPspReferenceAsync(string pspReference, CancellationToken ct) =>
            Task.FromResult(Notifications.Values.FirstOrDefault(n => n.PspReference == pspReference));

        public Task<AdyenNotification?> GetByIdAsync(Guid id, CancellationToken ct)
        {
            Notifications.TryGetValue(id, out var notification);
            return Task.FromResult(notification);
        }

        public Task<IReadOnlyList<AdyenNotification>> GetByMerchantReferenceAsync(string merchantReference, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AdyenNotification>>(Notifications.Values.Where(n => n.MerchantReference == merchantReference).ToList());

        public Task<IReadOnlyList<AdyenNotification>> GetUncorrelatedAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AdyenNotification>>(Notifications.Values.Where(n => n.CorrelatedPaymentId == null || n.Status == "Uncorrelated").ToList());

        public Task<IReadOnlyList<AdyenNotification>> GetUnprocessedCorrelatedAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AdyenNotification>>(Notifications.Values.Where(n => n.CorrelatedPaymentId != null && n.Status != "Processed").ToList());

        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private class FakePaymentRepository : IPaymentRepository
    {
        public readonly Dictionary<string, Payment> Payments = [];

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

        public Task<Payment?> GetByIdAsync(string id, CancellationToken ct = default)
        {
            Payments.TryGetValue(id, out var payment);
            return Task.FromResult(payment);
        }

        public Task<Payment?> GetByChannelReferenceAsync(string channelReference, CancellationToken ct = default) =>
            Task.FromResult(Payments.Values.FirstOrDefault(p => p.ChannelReference == channelReference));

        public Task<IReadOnlyList<Payment>> GetUnresolvedAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Payment>>(Payments.Values.Where(p => p.State == PaymentState.Pending || p.State == PaymentState.Unknown).ToList());
    }

    private class FakeSettlementPort : ISettlementPort
    {
        public SettlementResult QueryResult { get; set; } = SettlementResult.Unanswered("ADYEN", "Default mock");

        public Task<SettlementResult> AuthorizeAsync(string partyId, long amount, string currency, string? channel = null, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<SettlementResult> CaptureAsync(string paymentId, string channelReference, long amount, string currency, string? channel = null, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<SettlementResult> RefundAsync(string paymentId, string channelReference, long amount, string currency, string? channel = null, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<SettlementResult> CancelAsync(string paymentId, string channelReference, string? channel = null, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<SettlementResult> QueryPaymentStatusAsync(string paymentId, string? channelReference = null, string? merchantReference = null, string? channel = null, CancellationToken ct = default) =>
            Task.FromResult(QueryResult);
    }

    private class DirectMediator(IPaymentRepository paymentRepository) : IMediator
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is ApplyAcquirerOutcomeCommand cmd)
            {
                var handler = new ApplyAcquirerOutcomeCommandHandler(paymentRepository);
                var res = handler.Handle(cmd, cancellationToken).GetAwaiter().GetResult();
                return Task.FromResult((TResponse)(object)res);
            }
            throw new NotImplementedException();
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotImplementedException();
    }

    [Fact]
    public async Task AC3_WhenNotificationCannotBeCorrelated_RetainsForOps_AndDoesNotInventPayment()
    {
        // Arrange
        var notifRepo = new FakeNotificationRepository();
        var paymentRepo = new FakePaymentRepository();
        var settlementPort = new FakeSettlementPort();
        var mediator = new DirectMediator(paymentRepo);

        var notification = AdyenNotification.Create(
            pspReference: "psp_orphan_123",
            originalReference: null,
            merchantAccountCode: "InterparkingMockAccount",
            merchantReference: "unknown_merchant_ref",
            eventCode: "AUTHORISATION",
            eventDate: DateTimeOffset.UtcNow,
            amountValue: 1000,
            amountCurrency: "EUR",
            success: true,
            reason: null);

        await notifRepo.AddAsync(notification, CancellationToken.None);

        var service = new AdyenReconciliationService(notifRepo, paymentRepo, settlementPort, mediator);

        // Act
        var result = await service.ReconcileNotificationAsync(notification.Id);

        // Assert
        result.Status.Should().Be(ReconciliationStatus.Uncorrelated);
        paymentRepo.Payments.Should().BeEmpty(); // Did NOT invent any payment!
        notification.Status.Should().Be("Uncorrelated");

        var uncorrelated = await service.GetUncorrelatedNotificationsAsync();
        uncorrelated.Should().ContainSingle(n => n.Id == notification.Id);
    }

    [Fact]
    public async Task AC4_WhenSettlementCompleted_AndAdyenReportsAuthorised_ResolvesPendingPayment()
    {
        // Arrange
        var notifRepo = new FakeNotificationRepository();
        var paymentRepo = new FakePaymentRepository();
        var settlementPort = new FakeSettlementPort
        {
            QueryResult = SettlementResult.Success("ADYEN", "psp_auth_definitive", "mref_123")
        };
        var mediator = new DirectMediator(paymentRepo);

        var pendingPayment = Payment.CreatePending("cust_ac4", 5000L, "EUR", "ADYEN", "temp_channel_ref");
        await paymentRepo.AddAsync(pendingPayment);

        var service = new AdyenReconciliationService(notifRepo, paymentRepo, settlementPort, mediator);

        // Act
        var result = await service.ReconcileUnresolvedPaymentAsync(pendingPayment.Id);

        // Assert
        result.Status.Should().Be(ReconciliationStatus.Resolved);
        result.Outcome.Should().Be("Authorized");

        var updatedPayment = await paymentRepo.GetByIdAsync(pendingPayment.Id);
        updatedPayment!.State.Should().Be(PaymentState.Authorized);
        updatedPayment.ChannelReference.Should().Be("psp_auth_definitive");
    }

    [Fact]
    public async Task AC4_WhenAdyenReportsRefused_ResolvesPendingPaymentToDeclined()
    {
        // Arrange
        var notifRepo = new FakeNotificationRepository();
        var paymentRepo = new FakePaymentRepository();
        var settlementPort = new FakeSettlementPort
        {
            QueryResult = SettlementResult.Declined("ADYEN", "psp_refused_def", "Card expired", "mref_123")
        };
        var mediator = new DirectMediator(paymentRepo);

        var pendingPayment = Payment.CreatePending("cust_ac4_decline", 5000L, "EUR", "ADYEN", "temp_ref");
        await paymentRepo.AddAsync(pendingPayment);

        var service = new AdyenReconciliationService(notifRepo, paymentRepo, settlementPort, mediator);

        // Act
        var result = await service.ReconcileUnresolvedPaymentAsync(pendingPayment.Id);

        // Assert
        result.Status.Should().Be(ReconciliationStatus.Resolved);
        result.Outcome.Should().Be("Declined");

        var updatedPayment = await paymentRepo.GetByIdAsync(pendingPayment.Id);
        updatedPayment!.State.Should().Be(PaymentState.Declined);
        updatedPayment.DeclineReason.Should().Be("Card expired");
    }

    [Fact]
    public async Task AC4_WhenAdyenQueryUnanswered_LeavesPaymentUnresolvedAndExposesForOps()
    {
        // Arrange
        var notifRepo = new FakeNotificationRepository();
        var paymentRepo = new FakePaymentRepository();
        var settlementPort = new FakeSettlementPort
        {
            QueryResult = SettlementResult.Unanswered("ADYEN", "Adyen status query timed out", "mref_123")
        };
        var mediator = new DirectMediator(paymentRepo);

        var pendingPayment = Payment.CreatePending("cust_ac4_unans", 5000L, "EUR", "ADYEN", "temp_ref");
        await paymentRepo.AddAsync(pendingPayment);

        var service = new AdyenReconciliationService(notifRepo, paymentRepo, settlementPort, mediator);

        // Act
        var result = await service.ReconcileUnresolvedPaymentAsync(pendingPayment.Id);

        // Assert
        result.Status.Should().Be(ReconciliationStatus.Unresolved);

        var updatedPayment = await paymentRepo.GetByIdAsync(pendingPayment.Id);
        updatedPayment!.State.Should().Be(PaymentState.Pending); // Still Pending!

        var unresolvedList = await service.GetUnresolvedPaymentsAsync();
        unresolvedList.Should().ContainSingle(p => p.Id == pendingPayment.Id);
    }

    [Fact]
    public async Task AC2_And_AC5_WhenNotificationCorrelates_ResolvesPaymentAndMarksNotificationProcessed()
    {
        // Arrange
        var notifRepo = new FakeNotificationRepository();
        var paymentRepo = new FakePaymentRepository();
        var settlementPort = new FakeSettlementPort();
        var mediator = new DirectMediator(paymentRepo);

        var pendingPayment = Payment.CreatePending("cust_corr", 4200L, "EUR", "ADYEN");
        await paymentRepo.AddAsync(pendingPayment);

        var notification = AdyenNotification.Create(
            pspReference: "psp_webhook_corr_999",
            originalReference: null,
            merchantAccountCode: "InterparkingMockAccount",
            merchantReference: pendingPayment.Id,
            eventCode: "AUTHORISATION",
            eventDate: DateTimeOffset.UtcNow,
            amountValue: 4200,
            amountCurrency: "EUR",
            success: true,
            reason: null,
            correlatedPaymentId: pendingPayment.Id);

        await notifRepo.AddAsync(notification, CancellationToken.None);

        var service = new AdyenReconciliationService(notifRepo, paymentRepo, settlementPort, mediator);

        // Act
        var result = await service.ReconcileNotificationAsync(notification.Id);

        // Assert
        result.Status.Should().Be(ReconciliationStatus.Resolved);
        notification.Status.Should().Be("Processed");

        var updatedPayment = await paymentRepo.GetByIdAsync(pendingPayment.Id);
        updatedPayment!.State.Should().Be(PaymentState.Authorized);
    }
}
