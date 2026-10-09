using FluentAssertions;
using PaymentGateway.Application.Risk.Ports;
using PaymentGateway.Application.Risk.Queries;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using Xunit;

namespace PaymentGateway.UnitTests.Application;

public sealed class GetRiskVerdictQueryHandlerTests
{
    private sealed class StubVerdictRepository(RiskVerdict? verdict) : IRiskVerdictRepository
    {
        public Task<RiskVerdict?> GetByEventIdAsync(string eventId, CancellationToken ct = default) => Task.FromResult(verdict);
        public Task<RiskVerdict?> GetByPaymentIdAsync(string paymentId, CancellationToken ct = default) => Task.FromResult(verdict);
        public Task<RiskVerdict?> GetByIdentifierAsync(string identifier, CancellationToken ct = default) => Task.FromResult(verdict);
        public Task SaveAsync(RiskVerdict verdict, CancellationToken ct = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task Handle_Found_ReturnsDto()
    {
        var scoredAt = DateTimeOffset.UtcNow;
        var verdict = RiskVerdict.Clean("v1", "e1", "p1", "party-1", "clean tx", scoredAt);
        var handler = new GetRiskVerdictQueryHandler(new StubVerdictRepository(verdict));

        var result = await handler.Handle(new GetRiskVerdictQuery("p1"), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Id.Should().Be("v1");
        result.PaymentId.Should().Be("p1");
        result.EventId.Should().Be("e1");
        result.Status.Should().Be("clean");
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsNull()
    {
        var handler = new GetRiskVerdictQueryHandler(new StubVerdictRepository(null));
        var result = await handler.Handle(new GetRiskVerdictQuery("non-existent"), CancellationToken.None);

        result.Should().BeNull();
    }
}
