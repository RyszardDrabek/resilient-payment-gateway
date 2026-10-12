using FluentAssertions;
using PaymentGateway.Application.Payments;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Ports;
using Xunit;

namespace PaymentGateway.UnitTests.Application;

public sealed class GetUnresolvedPaymentsQueryHandlerTests
{
    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class RecordingPaymentRepository : IPaymentRepository
    {
        public DateTimeOffset LastCreatedBefore { get; private set; }
        public string? LastChannel { get; private set; }
        public int LastPage { get; private set; }
        public int LastPageSize { get; private set; }
        public IReadOnlyList<Payment> StubItems { get; set; } = [];
        public int StubTotalCount { get; set; }

        public Task AddAsync(Payment payment, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Payment payment, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Payment?> GetByIdAsync(string id, CancellationToken ct = default) => Task.FromResult<Payment?>(null);
        public Task<Payment?> GetByChannelReferenceAsync(string channelReference, CancellationToken ct = default) => Task.FromResult<Payment?>(null);

        public Task<(IReadOnlyList<Payment> Items, int TotalCount)> GetUnresolvedPaginatedAsync(
            DateTimeOffset createdBefore,
            string? channel = null,
            int page = 1,
            int pageSize = 50,
            CancellationToken ct = default)
        {
            LastCreatedBefore = createdBefore;
            LastChannel = channel;
            LastPage = page;
            LastPageSize = pageSize;
            return Task.FromResult((StubItems, StubTotalCount));
        }
    }

    [Fact]
    public async Task Handle_WithDefaultAgeMinutes_Computes15MinuteCutoffAndQueriesRepo()
    {
        var now = new DateTimeOffset(2026, 10, 12, 14, 0, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(now);
        var repo = new RecordingPaymentRepository();
        var handler = new GetUnresolvedPaymentsQueryHandler(repo, timeProvider);

        var query = new GetUnresolvedPaymentsQuery(Channel: null, AgeMinutes: null, Page: 1, PageSize: 50);
        var response = await handler.Handle(query, CancellationToken.None);

        repo.LastCreatedBefore.Should().Be(now.AddMinutes(-15));
        response.AgeMinutes.Should().Be(15);
        response.Page.Should().Be(1);
        response.PageSize.Should().Be(50);
    }

    [Fact]
    public async Task Handle_WithCustomAgeMinutes_PassesExpectedCutoff()
    {
        var now = new DateTimeOffset(2026, 10, 12, 14, 0, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(now);
        var repo = new RecordingPaymentRepository();
        var handler = new GetUnresolvedPaymentsQueryHandler(repo, timeProvider);

        var query = new GetUnresolvedPaymentsQuery(Channel: "adyen", AgeMinutes: 45, Page: 2, PageSize: 20);
        var response = await handler.Handle(query, CancellationToken.None);

        repo.LastCreatedBefore.Should().Be(now.AddMinutes(-45));
        repo.LastChannel.Should().Be("adyen");
        repo.LastPage.Should().Be(2);
        repo.LastPageSize.Should().Be(20);

        response.AgeMinutes.Should().Be(45);
        response.Channel.Should().Be("adyen");
        response.Page.Should().Be(2);
        response.PageSize.Should().Be(20);
    }

    [Fact]
    public async Task Handle_WithNegativeAgeMinutes_ClampsToZero()
    {
        var now = new DateTimeOffset(2026, 10, 12, 14, 0, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(now);
        var repo = new RecordingPaymentRepository();
        var handler = new GetUnresolvedPaymentsQueryHandler(repo, timeProvider);

        var query = new GetUnresolvedPaymentsQuery(AgeMinutes: -10);
        var response = await handler.Handle(query, CancellationToken.None);

        repo.LastCreatedBefore.Should().Be(now);
        response.AgeMinutes.Should().Be(0);
    }

    [Fact]
    public async Task Handle_ComputesDurationElapsedMinutesAccurately()
    {
        var now = new DateTimeOffset(2026, 10, 12, 14, 0, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(now);
        var repo = new RecordingPaymentRepository();

        var payment = new Payment(
            id: "pay_test_unres_1",
            partyId: "party_1",
            amount: 5000,
            currency: "EUR",
            settlementChannel: "ADYEN",
            state: PaymentState.Pending,
            channelReference: "psp_123");

        // Reflection or using payment with created time 30 minutes ago
        var field = typeof(Payment).GetProperty(nameof(Payment.CreatedAt));
        field?.SetValue(payment, now.AddMinutes(-30));

        repo.StubItems = [payment];
        repo.StubTotalCount = 1;

        var handler = new GetUnresolvedPaymentsQueryHandler(repo, timeProvider);
        var response = await handler.Handle(new GetUnresolvedPaymentsQuery(), CancellationToken.None);

        response.Items.Should().HaveCount(1);
        var item = response.Items[0];
        item.PaymentId.Should().Be("pay_test_unres_1");
        item.State.Should().Be("Pending");
        item.SettlementChannel.Should().Be("ADYEN");
        item.ChannelReference.Should().Be("psp_123");
        item.DurationElapsedMinutes.Should().Be(30);
    }

    [Theory]
    [InlineData(0, 50, 1, 50)]
    [InlineData(-5, 0, 1, 50)]
    [InlineData(2, 200, 2, 100)]
    public async Task Handle_ClampsPaginationProperly(int inputPage, int inputPageSize, int expectedPage, int expectedPageSize)
    {
        var now = DateTimeOffset.UtcNow;
        var timeProvider = new FixedTimeProvider(now);
        var repo = new RecordingPaymentRepository();
        var handler = new GetUnresolvedPaymentsQueryHandler(repo, timeProvider);

        var query = new GetUnresolvedPaymentsQuery(Page: inputPage, PageSize: inputPageSize);
        var response = await handler.Handle(query, CancellationToken.None);

        repo.LastPage.Should().Be(expectedPage);
        repo.LastPageSize.Should().Be(expectedPageSize);
        response.Page.Should().Be(expectedPage);
        response.PageSize.Should().Be(expectedPageSize);
    }
}
