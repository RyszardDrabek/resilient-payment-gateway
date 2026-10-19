using FluentAssertions;
using PaymentGateway.Application.PaymentMethods;
using PaymentGateway.Application.Subscriptions;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Ports;
using Xunit;

namespace PaymentGateway.UnitTests.Application;

public sealed class PaymentMethodsAndSubscriptionsHandlerTests
{
    [Fact]
    public async Task TokenizeAndGetPaymentMethods_WorkflowSucceeds()
    {
        var repo = new InMemoryPaymentMethodRepository();
        var tokenizeHandler = new TokenizePaymentMethodCommandHandler(repo);
        var getHandler = new GetPaymentMethodsQueryHandler(repo);

        var tokenDto = await tokenizeHandler.Handle(new TokenizePaymentMethodCommand(
            "party_test",
            "ADYEN",
            "visa",
            "**** **** **** 4242",
            12,
            2030,
            "tok_mock_123"), CancellationToken.None);

        tokenDto.Should().NotBeNull();
        tokenDto.PartyId.Should().Be("party_test");
        tokenDto.Status.Should().Be("Active");
        tokenDto.MaskedPan.Should().Be("**** **** **** 4242");

        var list = await getHandler.Handle(new GetPaymentMethodsQuery("party_test"), CancellationToken.None);
        list.Should().ContainSingle(m => m.Id == tokenDto.Id);
    }

    [Fact]
    public async Task RevokePaymentMethod_UpdatesStatusToRevoked()
    {
        var repo = new InMemoryPaymentMethodRepository();
        var token = new PaymentMethodToken("pm_1", "party_1", "ADYEN", "visa", "**** 1111", 10, 2030, "tok_1");
        await repo.SaveAsync(token);

        var revokeHandler = new RevokePaymentMethodCommandHandler(repo);
        var result = await revokeHandler.Handle(new RevokePaymentMethodCommand("pm_1"), CancellationToken.None);

        result.Should().BeTrue();
        var updated = await repo.GetByIdAsync("pm_1");
        updated!.Status.Should().Be(PaymentMethodStatus.Revoked);
    }

    [Fact]
    public async Task CreateSubscription_WithActivePaymentMethod_Succeeds()
    {
        var pmRepo = new InMemoryPaymentMethodRepository();
        var subRepo = new InMemorySubscriptionRepository();

        var token = new PaymentMethodToken("pm_valid", "party_1", "ADYEN", "visa", "**** 1111", 10, 2030, "tok_valid");
        await pmRepo.SaveAsync(token);

        var createHandler = new CreateSubscriptionCommandHandler(subRepo, pmRepo);
        var result = await createHandler.Handle(new CreateSubscriptionCommand(
            "party_1",
            "pm_valid",
            4900,
            "EUR",
            SubscriptionInterval.Monthly), CancellationToken.None);

        result.Should().NotBeNull();
        result.PartyId.Should().Be("party_1");
        result.PaymentMethodId.Should().Be("pm_valid");
        result.Amount.Should().Be(4900);
        result.State.Should().Be("Active");
    }

    [Fact]
    public async Task CreateSubscription_WithRevokedPaymentMethod_ThrowsInvalidOperationException()
    {
        var pmRepo = new InMemoryPaymentMethodRepository();
        var subRepo = new InMemorySubscriptionRepository();

        var token = new PaymentMethodToken("pm_revoked", "party_1", "ADYEN", "visa", "**** 1111", 10, 2030, "tok_revoked");
        token.Revoke();
        await pmRepo.SaveAsync(token);

        var createHandler = new CreateSubscriptionCommandHandler(subRepo, pmRepo);
        var act = () => createHandler.Handle(new CreateSubscriptionCommand(
            "party_1",
            "pm_revoked",
            4900,
            "EUR",
            SubscriptionInterval.Monthly), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SubscriptionLifecycle_PauseResumeCancel_WorksExpectedly()
    {
        var subRepo = new InMemorySubscriptionRepository();
        var sub = new Subscription("sub_1", "party_1", "pm_1", 1000, "EUR", SubscriptionInterval.Monthly);
        await subRepo.SaveAsync(sub);

        var lifecycleHandlers = new SubscriptionLifecycleCommandHandlers(subRepo);

        // Pause
        var paused = await lifecycleHandlers.Handle(new PauseSubscriptionCommand("sub_1"), CancellationToken.None);
        paused.State.Should().Be("Paused");

        // Resume
        var resumed = await lifecycleHandlers.Handle(new ResumeSubscriptionCommand("sub_1"), CancellationToken.None);
        resumed.State.Should().Be("Active");

        // Cancel
        var cancelled = await lifecycleHandlers.Handle(new CancelSubscriptionCommand("sub_1"), CancellationToken.None);
        cancelled.State.Should().Be("Cancelled");
    }

    private sealed class InMemoryPaymentMethodRepository : IPaymentMethodRepository
    {
        private readonly List<PaymentMethodToken> _items = [];

        public Task<PaymentMethodToken?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_items.FirstOrDefault(m => m.Id == id));

        public Task<IReadOnlyList<PaymentMethodToken>> ListByPartyIdAsync(string partyId, bool activeOnly = true, CancellationToken cancellationToken = default)
        {
            var query = _items.Where(m => m.PartyId == partyId);
            if (activeOnly)
            {
                query = query.Where(m => m.Status == PaymentMethodStatus.Active);
            }
            return Task.FromResult<IReadOnlyList<PaymentMethodToken>>(query.ToList());
        }

        public Task SaveAsync(PaymentMethodToken paymentMethod, CancellationToken cancellationToken = default)
        {
            _items.RemoveAll(m => m.Id == paymentMethod.Id);
            _items.Add(paymentMethod);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemorySubscriptionRepository : ISubscriptionRepository
    {
        private readonly List<Subscription> _items = [];

        public Task<Subscription?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_items.FirstOrDefault(s => s.Id == id));

        public Task<IReadOnlyList<Subscription>> ListByPartyIdAsync(string partyId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Subscription>>(_items.Where(s => s.PartyId == partyId).ToList());

        public Task<IReadOnlyList<Subscription>> ListDueSubscriptionsAsync(DateTimeOffset asOf, int limit = 50, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Subscription>>(_items.Where(s => s.State == SubscriptionState.Active && s.NextBillingAt <= asOf).Take(limit).ToList());

        public Task<IReadOnlyList<Subscription>> ListAsync(int skip = 0, int take = 50, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Subscription>>(_items.Skip(skip).Take(take).ToList());

        public Task SaveAsync(Subscription subscription, CancellationToken cancellationToken = default)
        {
            _items.RemoveAll(s => s.Id == subscription.Id);
            _items.Add(subscription);
            return Task.CompletedTask;
        }
    }
}
