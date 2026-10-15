using System.Net;
using System.Text.Json;
using PaymentGateway.Application.Webhooks;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Ports;
using Xunit;

namespace PaymentGateway.UnitTests.Application;

public sealed class MerchantWebhookDlqHandlersTests
{
    private sealed class InMemoryDlqRepository : IMerchantWebhookDeadLetterRepository
    {
        public readonly Dictionary<Guid, MerchantWebhookDeadLetter> Records = [];

        public Task AddAsync(MerchantWebhookDeadLetter deadLetter, CancellationToken ct = default)
        {
            Records[deadLetter.Id] = deadLetter;
            return Task.CompletedTask;
        }

        public Task<MerchantWebhookDeadLetter?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            Records.TryGetValue(id, out var record);
            return Task.FromResult(record);
        }

        public Task<IReadOnlyList<MerchantWebhookDeadLetter>> ListByMerchantAsync(
            string merchantId,
            string? status = null,
            int page = 1,
            int pageSize = 50,
            CancellationToken ct = default)
        {
            var query = Records.Values
                .Where(r => string.Equals(r.MerchantId, merchantId, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(r => string.Equals(r.Status, status, StringComparison.OrdinalIgnoreCase));
            }

            var list = query
                .OrderByDescending(r => r.FailedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return Task.FromResult<IReadOnlyList<MerchantWebhookDeadLetter>>(list);
        }

        public Task UpdateAsync(MerchantWebhookDeadLetter deadLetter, CancellationToken ct = default)
        {
            Records[deadLetter.Id] = deadLetter;
            return Task.CompletedTask;
        }
    }

    private sealed class InMemorySubscriptionRepository : IMerchantWebhookSubscriptionRepository
    {
        public readonly Dictionary<string, MerchantWebhookSubscription> Subscriptions = [];

        public Task<MerchantWebhookSubscription?> GetActiveByPartyIdAsync(string partyId, CancellationToken ct = default)
        {
            Subscriptions.TryGetValue(partyId, out var sub);
            return Task.FromResult(sub is not null && sub.IsActive ? sub : null);
        }

        public Task<MerchantWebhookSubscription?> GetByPartyIdAsync(string partyId, CancellationToken ct = default)
        {
            Subscriptions.TryGetValue(partyId, out var sub);
            return Task.FromResult(sub);
        }

        public Task AddAsync(MerchantWebhookSubscription subscription, CancellationToken ct = default)
        {
            Subscriptions[subscription.PartyId] = subscription;
            return Task.CompletedTask;
        }

        public Task UpsertAsync(MerchantWebhookSubscription subscription, CancellationToken ct = default)
        {
            Subscriptions[subscription.PartyId] = subscription;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpRequestMessage? CapturedRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedRequest = request;
            return Task.FromResult(response);
        }
    }

    private readonly InMemoryDlqRepository _dlqRepository = new();
    private readonly InMemorySubscriptionRepository _subscriptionRepository = new();
    private readonly IMerchantWebhookSigner _signer = new HmacSha256WebhookSigner();

    [Fact]
    public async Task ListMerchantWebhookDlqQueryHandler_ReturnsRecordsFilteredByStatus()
    {
        // Arrange
        var r1 = MerchantWebhookDeadLetter.Create("evt_1", "pay_1", "merch_acme", "https://example.com/1", "{}", 5, "Err 1");
        var r2 = MerchantWebhookDeadLetter.Create("evt_2", "pay_2", "merch_acme", "https://example.com/2", "{}", 5, "Err 2");
        r2.MarkReplayed();

        await _dlqRepository.AddAsync(r1);
        await _dlqRepository.AddAsync(r2);

        var handler = new ListMerchantWebhookDlqQueryHandler(_dlqRepository);

        // Act - Query exhausted only
        var exhaustedOnly = await handler.Handle(
            new ListMerchantWebhookDlqQuery("merch_acme", Status: "exhausted"),
            CancellationToken.None);

        // Assert
        Assert.Single(exhaustedOnly);
        Assert.Equal("evt_1", exhaustedOnly[0].EventId);
        Assert.Equal("exhausted", exhaustedOnly[0].Status);

        // Act - Query all
        var allRecords = await handler.Handle(
            new ListMerchantWebhookDlqQuery("merch_acme"),
            CancellationToken.None);

        Assert.Equal(2, allRecords.Count);
    }

    [Fact]
    public async Task RecordMerchantWebhookDeadLetterCommandHandler_PersistsRecord()
    {
        // Arrange
        var handler = new RecordMerchantWebhookDeadLetterCommandHandler(_dlqRepository);
        var command = new RecordMerchantWebhookDeadLetterCommand(
            EventId: "evt_new",
            PaymentId: "pay_new",
            MerchantId: "merch_acme",
            DestinationUrl: "https://example.com/hook",
            PayloadJson: "{\"amount\":500}",
            DeliveryAttempts: 5,
            LastErrorMessage: "HTTP 503 Unavailable",
            LastStatusCode: 503);

        // Act
        var dlqId = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotEqual(Guid.Empty, dlqId);
        Assert.True(_dlqRepository.Records.ContainsKey(dlqId));
        var saved = _dlqRepository.Records[dlqId];
        Assert.Equal("evt_new", saved.EventId);
        Assert.Equal(503, saved.LastStatusCode);
    }

    [Fact]
    public async Task ReplayMerchantWebhookCommandHandler_SuccessfulReplay_MarksRecordReplayed()
    {
        // Arrange
        var record = MerchantWebhookDeadLetter.Create(
            "evt_replay",
            "pay_replay",
            "merch_acme",
            "https://merchant.example.com/webhook",
            "{\"amount\":1200}",
            5,
            "Timeout",
            null);

        await _dlqRepository.AddAsync(record);

        var sub = new MerchantWebhookSubscription(Guid.NewGuid(), "merch_acme", "https://merchant.example.com/webhook", "test-secret-123");
        await _subscriptionRepository.AddAsync(sub);

        var httpHandler = new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var httpClient = new HttpClient(httpHandler);

        var handler = new ReplayMerchantWebhookCommandHandler(
            _dlqRepository,
            _subscriptionRepository,
            _signer,
            httpClient);

        var command = new ReplayMerchantWebhookCommand("merch_acme", record.Id);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.Equal("replayed", record.Status);
        Assert.NotNull(record.ReplayedAt);
        Assert.Equal(1, record.ReplayAttemptCount);
        Assert.Equal(200, record.LastReplayStatusCode);

        // Verify headers
        Assert.NotNull(httpHandler.CapturedRequest);
        Assert.True(httpHandler.CapturedRequest!.Headers.Contains("X-Signature-SHA256"));
        Assert.True(httpHandler.CapturedRequest.Headers.Contains("X-Event-Id"));
        Assert.True(httpHandler.CapturedRequest.Headers.Contains("X-Timestamp"));
    }

    [Fact]
    public async Task ReplayMerchantWebhookCommandHandler_FailedReplay_RecordsAttemptAndStatusCode()
    {
        // Arrange
        var record = MerchantWebhookDeadLetter.Create(
            "evt_replay_fail",
            "pay_replay_fail",
            "merch_acme",
            "https://merchant.example.com/webhook",
            "{\"amount\":1200}",
            5,
            "Timeout",
            null);

        await _dlqRepository.AddAsync(record);

        var httpHandler = new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var httpClient = new HttpClient(httpHandler);

        var handler = new ReplayMerchantWebhookCommandHandler(
            _dlqRepository,
            _subscriptionRepository,
            _signer,
            httpClient);

        var command = new ReplayMerchantWebhookCommand("merch_acme", record.Id);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.False(result!.Success);
        Assert.Equal("exhausted", record.Status);
        Assert.Null(record.ReplayedAt);
        Assert.Equal(1, record.ReplayAttemptCount);
        Assert.Equal(500, record.LastReplayStatusCode);
    }

    [Fact]
    public async Task ReplayMerchantWebhookCommandHandler_WhenRecordNotFoundOrDifferentMerchant_ReturnsNull()
    {
        // Arrange
        var record = MerchantWebhookDeadLetter.Create("evt_diff", "pay_diff", "merch_other", "https://example.com", "{}", 1, "Err");
        await _dlqRepository.AddAsync(record);

        var handler = new ReplayMerchantWebhookCommandHandler(
            _dlqRepository,
            _subscriptionRepository,
            _signer,
            new HttpClient());

        // Act
        var result = await handler.Handle(new ReplayMerchantWebhookCommand("merch_acme", record.Id), CancellationToken.None);

        // Assert
        Assert.Null(result);
    }
}
