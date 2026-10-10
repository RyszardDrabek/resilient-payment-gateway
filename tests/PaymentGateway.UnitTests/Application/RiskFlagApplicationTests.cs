using FluentAssertions;
using PaymentGateway.Application.Risk.Commands;
using PaymentGateway.Application.Risk.Ports;
using PaymentGateway.Application.Risk.Queries;
using PaymentGateway.Domain.Entities;
using Xunit;

namespace PaymentGateway.UnitTests.Application;

public sealed class RiskFlagApplicationTests
{
    private sealed class InMemoryRiskFlagRepository : IRiskFlagRepository
    {
        public readonly List<RiskFlag> Flags = [];

        public Task<RiskFlag?> GetByIdAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(Flags.FirstOrDefault(f => f.Id == id));

        public Task<IReadOnlyList<RiskFlag>> GetByPaymentIdAsync(string paymentId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RiskFlag>>(Flags.Where(f => f.PaymentId == paymentId).ToList());

        public Task<(IReadOnlyList<RiskFlag> Items, int TotalCount)> GetOpenFlagsAsync(int page = 1, int pageSize = 50, CancellationToken ct = default)
        {
            var open = Flags.Where(f => f.Status == "Open" && f.Disposition == null).ToList();
            var items = open.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Task.FromResult<(IReadOnlyList<RiskFlag> Items, int TotalCount)>((items, open.Count));
        }

        public Task SaveAsync(RiskFlag flag, CancellationToken ct = default)
        {
            var idx = Flags.FindIndex(f => f.Id == flag.Id);
            if (idx >= 0)
            {
                Flags[idx] = flag;
            }
            else
            {
                Flags.Add(flag);
            }
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ListOpenRiskFlagsQueryHandler_ReturnsOnlyOpenFlags_WithPagination()
    {
        var repo = new InMemoryRiskFlagRepository();
        var now = DateTimeOffset.UtcNow;
        var f1 = new RiskFlag("flag-1", "p1", "party-1", "e1", "Reason 1", now.AddMinutes(-5));
        var f2 = new RiskFlag("flag-2", "p2", "party-1", "e2", "Reason 2", now.AddMinutes(-4));
        var f3 = new RiskFlag("flag-3", "p3", "party-2", "e3", "Reason 3", now.AddMinutes(-3));
        f3.ApplyDisposition("confirmed", now);

        repo.Flags.AddRange([f1, f2, f3]);

        var handler = new ListOpenRiskFlagsQueryHandler(repo);
        var result = await handler.Handle(new ListOpenRiskFlagsQuery(Page: 1, PageSize: 10), CancellationToken.None);

        result.TotalCount.Should().Be(2);
        result.Items.Should().HaveCount(2);
        result.Items.Select(x => x.Id).Should().Contain(["flag-1", "flag-2"]);
        result.Items.Should().NotContain(x => x.Id == "flag-3");
    }

    [Theory]
    [InlineData("confirmed")]
    [InlineData("false_positive")]
    [InlineData("escalated")]
    public async Task DispositionRiskFlagCommandHandler_Success_UpdatesAndPersistsFlag(string disposition)
    {
        var repo = new InMemoryRiskFlagRepository();
        var now = DateTimeOffset.UtcNow;
        var flag = new RiskFlag("flag-10", "p10", "party-10", "e10", "Reason 10", now);
        repo.Flags.Add(flag);

        var handler = new DispositionRiskFlagCommandHandler(repo);
        var result = await handler.Handle(new DispositionRiskFlagCommand("flag-10", disposition), CancellationToken.None);

        result.Outcome.Should().Be(DispositionOutcome.Success);
        result.Flag.Should().NotBeNull();
        result.Flag!.Disposition.Should().Be(disposition.ToLowerInvariant());
        result.Flag.Status.Should().Be("Dispositioned");
        result.Flag.DispositionedAt.Should().NotBeNull();

        var saved = await repo.GetByIdAsync("flag-10");
        saved!.IsDispositioned.Should().BeTrue();
        saved.Disposition.Should().Be(disposition.ToLowerInvariant());
    }

    [Fact]
    public async Task DispositionRiskFlagCommandHandler_WithNotes_PersistsReviewerNotes()
    {
        var repo = new InMemoryRiskFlagRepository();
        var now = DateTimeOffset.UtcNow;
        var flag = new RiskFlag("flag-10", "p10", "party-10", "e10", "Reason 10", now);
        repo.Flags.Add(flag);

        var handler = new DispositionRiskFlagCommandHandler(repo);
        var result = await handler.Handle(new DispositionRiskFlagCommand("flag-10", "confirmed", "Confirmed fraud case"), CancellationToken.None);

        result.Outcome.Should().Be(DispositionOutcome.Success);
        result.Flag!.ReviewerNotes.Should().Be("Confirmed fraud case");

        var saved = await repo.GetByIdAsync("flag-10");
        saved!.ReviewerNotes.Should().Be("Confirmed fraud case");
    }

    [Fact]
    public async Task DispositionRiskFlagCommandHandler_NotFound_ReturnsNotFoundOutcome()
    {
        var repo = new InMemoryRiskFlagRepository();
        var handler = new DispositionRiskFlagCommandHandler(repo);

        var result = await handler.Handle(new DispositionRiskFlagCommand("non-existent-flag", "confirmed"), CancellationToken.None);

        result.Outcome.Should().Be(DispositionOutcome.NotFound);
        result.Flag.Should().BeNull();
        result.ErrorMessage.Should().Contain("non-existent-flag");
    }

    [Fact]
    public async Task DispositionRiskFlagCommandHandler_AlreadyDispositioned_ReturnsAlreadyDispositionedOutcome()
    {
        var repo = new InMemoryRiskFlagRepository();
        var now = DateTimeOffset.UtcNow;
        var flag = new RiskFlag("flag-10", "p10", "party-10", "e10", "Reason 10", now);
        flag.ApplyDisposition("confirmed", now);
        repo.Flags.Add(flag);

        var handler = new DispositionRiskFlagCommandHandler(repo);
        var result = await handler.Handle(new DispositionRiskFlagCommand("flag-10", "escalated"), CancellationToken.None);

        result.Outcome.Should().Be(DispositionOutcome.AlreadyDispositioned);
        result.Flag.Should().BeNull();
        result.ErrorMessage.Should().Contain("already been dispositioned");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid_disposition")]
    public async Task DispositionRiskFlagCommandHandler_InvalidDisposition_ReturnsInvalidDispositionOutcome(string invalidDisposition)
    {
        var repo = new InMemoryRiskFlagRepository();
        var now = DateTimeOffset.UtcNow;
        var flag = new RiskFlag("flag-10", "p10", "party-10", "e10", "Reason 10", now);
        repo.Flags.Add(flag);

        var handler = new DispositionRiskFlagCommandHandler(repo);
        var result = await handler.Handle(new DispositionRiskFlagCommand("flag-10", invalidDisposition), CancellationToken.None);

        result.Outcome.Should().Be(DispositionOutcome.InvalidDisposition);
        result.Flag.Should().BeNull();
    }
}
