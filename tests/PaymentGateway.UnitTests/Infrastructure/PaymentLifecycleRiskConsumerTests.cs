using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.Events;
using PaymentGateway.Application.Risk.Commands;
using PaymentGateway.Application.Risk.Ports;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Infrastructure.Consumers;
using PaymentGateway.Infrastructure.Risk;
using Xunit;

namespace PaymentGateway.UnitTests.Infrastructure;

public sealed class PaymentLifecycleRiskConsumerTests
{
    private sealed class StubVerdictRepository : IRiskVerdictRepository
    {
        public readonly List<RiskVerdict> Saved = [];

        public Task<RiskVerdict?> GetByEventIdAsync(string eventId, CancellationToken ct = default) =>
            Task.FromResult(Saved.FirstOrDefault(v => v.EventId == eventId));

        public Task<RiskVerdict?> GetByPaymentIdAsync(string paymentId, CancellationToken ct = default) =>
            Task.FromResult(Saved.FirstOrDefault(v => v.PaymentId == paymentId));

        public Task<RiskVerdict?> GetByIdentifierAsync(string identifier, CancellationToken ct = default) =>
            Task.FromResult(Saved.FirstOrDefault(v => v.PaymentId == identifier || v.EventId == identifier));

        public Task SaveAsync(RiskVerdict verdict, CancellationToken ct = default)
        {
            Saved.Add(verdict);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeMediator(Func<IRequest<RiskVerdict>, CancellationToken, Task<RiskVerdict>> handler) : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            handler((IRequest<RiskVerdict>)request, cancellationToken) as Task<TResponse>
            ?? throw new InvalidCastException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest =>
            Task.CompletedTask;

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    [Fact]
    public async Task ProcessEventAsync_SuccessfulScoring_ReturnsVerdict()
    {
        var expected = RiskVerdict.Clean("v1", "e1", "p1", "party-1", "all good", DateTimeOffset.UtcNow);
        var mediator = new FakeMediator((req, ct) => Task.FromResult(expected));
        var verdictRepo = new StubVerdictRepository();
        var options = Options.Create(new RiskOptions { ScoreTimeoutSeconds = 5 });
        var consumer = new PaymentLifecycleRiskConsumer(mediator, verdictRepo, options, NullLogger<PaymentLifecycleRiskConsumer>.Instance);

        var evt = new PaymentLifecycleEvent("e1", "p1", "party-1", "Authorized", 1000, "EUR", 1, DateTimeOffset.UtcNow);
        var verdict = await consumer.ProcessEventAsync(evt);

        verdict.Status.Should().Be(RiskVerdictStatus.Clean);
        verdict.EventId.Should().Be("e1");
    }

    [Fact]
    public async Task ProcessEventAsync_MediatorThrowsException_FailsOpenAndSavesDegradedVerdict()
    {
        var mediator = new FakeMediator((req, ct) => throw new InvalidOperationException("External model unavailable"));
        var verdictRepo = new StubVerdictRepository();
        var options = Options.Create(new RiskOptions { ScoreTimeoutSeconds = 5 });
        var consumer = new PaymentLifecycleRiskConsumer(mediator, verdictRepo, options, NullLogger<PaymentLifecycleRiskConsumer>.Instance);

        var evt = new PaymentLifecycleEvent("e2", "p2", "party-2", "Authorized", 1000, "EUR", 1, DateTimeOffset.UtcNow);

        // Must not throw (fail-open)
        var verdict = await consumer.ProcessEventAsync(evt);

        verdict.Status.Should().Be(RiskVerdictStatus.Degraded);
        verdict.EventId.Should().Be("e2");
        verdict.PaymentId.Should().Be("p2");
        verdict.Reason.Should().Contain("External model unavailable");

        verdictRepo.Saved.Should().ContainSingle();
        verdictRepo.Saved[0].Status.Should().Be(RiskVerdictStatus.Degraded);
    }
}
