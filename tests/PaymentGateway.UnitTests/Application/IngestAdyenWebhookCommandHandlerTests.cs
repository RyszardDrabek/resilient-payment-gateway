using FluentAssertions;
using PaymentGateway.Application.Adyen;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Ports;
using Xunit;

namespace PaymentGateway.UnitTests.Application;

public sealed class IngestAdyenWebhookCommandHandlerTests
{
    private readonly FakeHmacValidator _hmacValidator = new();
    private readonly FakeAdyenNotificationRepository _notificationRepository = new();
    private readonly FakePaymentRepository _paymentRepository = new();
    private readonly IngestAdyenWebhookCommandHandler _handler;

    public IngestAdyenWebhookCommandHandlerTests()
    {
        _handler = new IngestAdyenWebhookCommandHandler(
            _hmacValidator,
            _notificationRepository,
            _paymentRepository);
    }

    [Fact]
    public async Task Handle_WithValidHmac_IngestsSuccessfully()
    {
        _hmacValidator.IsValid = true;
        var item = CreateTestItem("PSP-1", "PAY-1", "AUTHORISATION");
        var payload = CreatePayload(item);

        var result = await _handler.Handle(new IngestAdyenWebhookCommand(payload), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _notificationRepository.StoredNotifications.Should().HaveCount(1);
        _notificationRepository.StoredNotifications[0].PspReference.Should().Be("PSP-1");
    }

    [Fact]
    public async Task Handle_WithInvalidHmac_RejectsAndDoesNotStore()
    {
        _hmacValidator.IsValid = false;
        var item = CreateTestItem("PSP-1", "PAY-1", "AUTHORISATION");
        var payload = CreatePayload(item);

        var result = await _handler.Handle(new IngestAdyenWebhookCommand(payload), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.IsInvalidHmac.Should().BeTrue();
        _notificationRepository.StoredNotifications.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithReplay_RejectsAndDoesNotStoreDuplicate()
    {
        _hmacValidator.IsValid = true;
        var item = CreateTestItem("PSP-1", "PAY-1", "AUTHORISATION");
        _notificationRepository.ExistingKeys.Add(("PSP-1", "AUTHORISATION"));
        var payload = CreatePayload(item);

        var result = await _handler.Handle(new IngestAdyenWebhookCommand(payload), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.IsReplay.Should().BeTrue();
        _notificationRepository.StoredNotifications.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithMatchingMerchantReference_SetsCorrelatedPaymentId()
    {
        _hmacValidator.IsValid = true;
        var payment = new Payment(
            id: "PAY-100",
            partyId: "PARTY-1",
            amount: 5000,
            currency: "EUR",
            settlementChannel: "ADYEN",
            state: PaymentState.Authorized,
            channelReference: "PSP-OLD");
        _paymentRepository.Payments.Add(payment);

        var item = CreateTestItem("PSP-NEW", "PAY-100", "AUTHORISATION");
        var payload = CreatePayload(item);

        var result = await _handler.Handle(new IngestAdyenWebhookCommand(payload), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _notificationRepository.StoredNotifications.Should().HaveCount(1);
        _notificationRepository.StoredNotifications[0].CorrelatedPaymentId.Should().Be("PAY-100");
    }

    [Fact]
    public async Task Handle_WithMatchingPspReference_SetsCorrelatedPaymentId()
    {
        _hmacValidator.IsValid = true;
        var payment = new Payment(
            id: "PAY-200",
            partyId: "PARTY-2",
            amount: 5000,
            currency: "EUR",
            settlementChannel: "ADYEN",
            state: PaymentState.Authorized,
            channelReference: "PSP-MATCH");
        _paymentRepository.Payments.Add(payment);

        var item = CreateTestItem("PSP-MATCH", "UNKNOWN-MERCHANT-REF", "CAPTURE");
        var payload = CreatePayload(item);

        var result = await _handler.Handle(new IngestAdyenWebhookCommand(payload), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _notificationRepository.StoredNotifications.Should().HaveCount(1);
        _notificationRepository.StoredNotifications[0].CorrelatedPaymentId.Should().Be("PAY-200");
    }

    private static AdyenNotificationRequestItem CreateTestItem(string pspRef, string merchantRef, string eventCode) =>
        new(
            AdditionalData: new Dictionary<string, string> { ["hmacSignature"] = "dummy" },
            Amount: new AdyenAmountDto("EUR", 1000),
            EventCode: eventCode,
            EventDate: DateTimeOffset.UtcNow,
            MerchantAccountCode: "TestAccount",
            MerchantReference: merchantRef,
            OriginalReference: null,
            PspReference: pspRef,
            Reason: null,
            Success: "true");

    private static AdyenWebhookPayload CreatePayload(params AdyenNotificationRequestItem[] items) =>
        new(
            Live: "false",
            NotificationItems: items.Select(x => new AdyenNotificationItemWrapper(x)).ToList());

    private sealed class FakeHmacValidator : IAdyenHmacValidator
    {
        public bool IsValid { get; set; } = true;
        public bool Validate(AdyenNotificationRequestItem item, string? hmacKeyOverride = null) => IsValid;
        public string CalculateSignature(AdyenNotificationRequestItem item, string? hmacKeyOverride = null) => "calculated_sig";
    }

    private sealed class FakeAdyenNotificationRepository : IAdyenNotificationRepository
    {
        public List<AdyenNotification> StoredNotifications { get; } = [];
        public HashSet<(string PspRef, string EventCode)> ExistingKeys { get; } = [];

        public Task<bool> ExistsAsync(string pspReference, string eventCode, CancellationToken ct) =>
            Task.FromResult(ExistingKeys.Contains((pspReference, eventCode)));

        public Task AddAsync(AdyenNotification notification, CancellationToken ct)
        {
            StoredNotifications.Add(notification);
            return Task.CompletedTask;
        }

        public Task<AdyenNotification?> GetByPspReferenceAsync(string pspReference, CancellationToken ct) =>
            Task.FromResult(StoredNotifications.FirstOrDefault(n => n.PspReference == pspReference));

        public Task<IReadOnlyList<AdyenNotification>> GetByMerchantReferenceAsync(string merchantReference, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AdyenNotification>>(StoredNotifications.Where(n => n.MerchantReference == merchantReference).ToList());

        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakePaymentRepository : IPaymentRepository
    {
        public List<Payment> Payments { get; } = [];

        public Task AddAsync(Payment payment, CancellationToken ct = default)
        {
            Payments.Add(payment);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Payment payment, CancellationToken ct = default) => Task.CompletedTask;

        public Task<Payment?> GetByIdAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(Payments.FirstOrDefault(p => p.Id == id));

        public Task<Payment?> GetByChannelReferenceAsync(string channelReference, CancellationToken ct = default) =>
            Task.FromResult(Payments.FirstOrDefault(p => p.ChannelReference == channelReference));
    }
}
