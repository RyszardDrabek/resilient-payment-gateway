using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.Payments;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Events;
using PaymentGateway.Domain.Ports;
using PaymentGateway.Domain.Services;
using PaymentGateway.Infrastructure.Adyen;
using PaymentGateway.Infrastructure.Adyen.Models;
using Xunit;

namespace PaymentGateway.UnitTests.Infrastructure;

public sealed class AdyenProviderFailurePolicyTests
{
    private sealed class DelegatingTestHandler(Func<HttpRequestMessage, HttpResponseMessage> handlerFunc) : HttpMessageHandler
    {
        public List<HttpRequestMessage> CapturedRequests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedRequests.Add(request);
            return Task.FromResult(handlerFunc(request));
        }
    }

    [Fact]
    public async Task AC1_WhenAcquirerReturnsTransient503_RetriesUnderPolicyAndReusesSameMerchantReference()
    {
        // Arrange
        var attempt = 0;
        var capturedBodies = new List<string>();

        var testHandler = new DelegatingTestHandler(req =>
        {
            attempt++;
            var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
            capturedBodies.Add(body);

            if (attempt == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("{\"error\":\"upstream server temporarily unavailable\"}")
                };
            }

            var successPayload = new AdyenPaymentResponse("psp_retry_success_1", "Authorised", null, "mref_test");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(successPayload))
            };
        });

        var client = new HttpClient(testHandler);
        var options = Options.Create(new AdyenOptions
        {
            BaseUrl = "http://mock-adyen.local",
            MaxRetryAttempts = 2,
            RetryDelayMilliseconds = 5
        });

        var port = new AdyenSettlementPort(client, options, logger: NullLogger<AdyenSettlementPort>.Instance);

        // Act
        var result = await port.AuthorizeAsync("party_retry_test", 5000, "EUR", null, "idem_ac1_test");

        // Assert
        result.IsSuccessful.Should().BeTrue();
        result.IsAuthorized.Should().BeTrue();
        result.ChannelReference.Should().Be("psp_retry_success_1");
        attempt.Should().Be(2, "system should have retried the transient 503 failure once");

        // Verify AC-1: Retries reuse the same merchant reference so no second settlement attempt is created
        capturedBodies.Should().HaveCount(2);
        using var doc1 = JsonDocument.Parse(capturedBodies[0]);
        using var doc2 = JsonDocument.Parse(capturedBodies[1]);
        var ref1 = doc1.RootElement.GetProperty("reference").GetString();
        var ref2 = doc2.RootElement.GetProperty("reference").GetString();

        ref1.Should().NotBeNullOrWhiteSpace();
        ref2.Should().Be(ref1, "all retry attempts within policy must share the exact same merchant reference");
        ref1.Should().Contain("idem_ac1_test");
    }

    [Fact]
    public async Task AC1_WhenAcquirerReturnsRateLimit429_RetriesAndSucceeds()
    {
        // Arrange
        var attempt = 0;
        var testHandler = new DelegatingTestHandler(req =>
        {
            attempt++;
            if (attempt == 1)
            {
                return new HttpResponseMessage((HttpStatusCode)429)
                {
                    Content = new StringContent("{\"message\":\"Too many requests\"}")
                };
            }

            var successPayload = new AdyenPaymentResponse("psp_429_recovered", "Authorised", null, "mref_429");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(successPayload))
            };
        });

        var client = new HttpClient(testHandler);
        var options = Options.Create(new AdyenOptions
        {
            BaseUrl = "http://mock-adyen.local",
            MaxRetryAttempts = 2,
            RetryDelayMilliseconds = 5
        });

        var port = new AdyenSettlementPort(client, options, logger: NullLogger<AdyenSettlementPort>.Instance);

        // Act
        var result = await port.AuthorizeAsync("party_rate_limit", 7500, "EUR", null, "idem_429");

        // Assert
        result.IsAuthorized.Should().BeTrue();
        attempt.Should().Be(2);
    }

    [Fact]
    public async Task AC2_WhenAcquirerReturnsTerminalRefused_StopsImmediatelyWithoutRetrying()
    {
        // Arrange
        var attempt = 0;
        var testHandler = new DelegatingTestHandler(req =>
        {
            attempt++;
            var declinePayload = new AdyenPaymentResponse("psp_declined_1", "Refused", "Insufficient funds", "mref_dec");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(declinePayload))
            };
        });

        var client = new HttpClient(testHandler);
        var options = Options.Create(new AdyenOptions
        {
            BaseUrl = "http://mock-adyen.local",
            MaxRetryAttempts = 3,
            RetryDelayMilliseconds = 5
        });

        var port = new AdyenSettlementPort(client, options);

        // Act
        var result = await port.AuthorizeAsync("decline_party", 3000, "EUR", null, "idem_terminal_dec");

        // Assert
        result.IsAuthorized.Should().BeFalse();
        result.IsUnanswered.Should().BeFalse();
        result.DeclineReason.Should().Be("Insufficient funds");
        attempt.Should().Be(1, "terminal declines must not be retried");
    }

    [Fact]
    public async Task AC2_WhenAcquirerReturnsTerminalBadRequest400_StopsImmediatelyWithoutRetrying()
    {
        // Arrange
        var attempt = 0;
        var testHandler = new DelegatingTestHandler(req =>
        {
            attempt++;
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"error\":\"Invalid currency code\"}")
            };
        });

        var client = new HttpClient(testHandler);
        var options = Options.Create(new AdyenOptions
        {
            BaseUrl = "http://mock-adyen.local",
            MaxRetryAttempts = 3,
            RetryDelayMilliseconds = 5
        });

        var port = new AdyenSettlementPort(client, options);

        // Act
        var result = await port.AuthorizeAsync("party_bad_request", 2000, "EUR");

        // Assert
        result.IsAuthorized.Should().BeFalse();
        result.IsUnanswered.Should().BeFalse();
        result.DeclineReason.Should().Contain("400");
        attempt.Should().Be(1, "terminal client errors must not be retried");
    }

    [Fact]
    public async Task AC2_WhenAcquirerReturnsTerminal401Unauthorized_StopsWithoutRetryingAndSurfacesConfigurationFail()
    {
        // Arrange
        var attempt = 0;
        var testHandler = new DelegatingTestHandler(req =>
        {
            attempt++;
            return new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{\"status\":401,\"message\":\"Invalid API key\"}")
            };
        });

        var client = new HttpClient(testHandler);
        var options = Options.Create(new AdyenOptions
        {
            BaseUrl = "http://mock-adyen.local",
            MaxRetryAttempts = 3,
            RetryDelayMilliseconds = 5
        });

        var port = new AdyenSettlementPort(client, options);

        // Act
        var result = await port.AuthorizeAsync("party_unauth", 1000, "EUR");

        // Assert
        result.IsAuthorized.Should().BeFalse();
        result.IsConfigurationFail.Should().BeTrue();
        result.IsUnanswered.Should().BeFalse();
        attempt.Should().Be(1, "authentication errors must not be retried");
    }

    [Fact]
    public async Task AC3_WhenAcquirerExhaustsRetries_ReturnsUnansweredPastDeadline()
    {
        // Arrange
        var attempt = 0;
        var testHandler = new DelegatingTestHandler(req =>
        {
            attempt++;
            return new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("{\"status\":500,\"message\":\"Persistent internal error\"}")
            };
        });

        var client = new HttpClient(testHandler);
        var options = Options.Create(new AdyenOptions
        {
            BaseUrl = "http://mock-adyen.local",
            MaxRetryAttempts = 2,
            RetryDelayMilliseconds = 5
        });

        var port = new AdyenSettlementPort(client, options);

        // Act
        var result = await port.AuthorizeAsync("party_persistent_fail", 4000, "EUR");

        // Assert
        result.IsAuthorized.Should().BeFalse();
        result.IsUnanswered.Should().BeTrue();
        result.DeclineReason.Should().Contain("500");
        attempt.Should().Be(3, "initial attempt + 2 retries should execute before marking unanswered");
    }

    [Fact]
    public async Task AC3_WhenUnansweredPastDeadline_ApplicationHandlerLeavesPaymentPendingAndEmitsDomainEvent()
    {
        // Arrange
        var mockSettlementPort = new StubSettlementPort
        {
            ReturnsUnanswered = true,
            DeclineReason = "Acquirer connection timeout after policy retries"
        };
        var paymentRepo = new InMemoryPaymentRepo();
        var idemRepo = new InMemoryIdemRepo();
        var handler = new AuthorizePaymentCommandHandler(mockSettlementPort, paymentRepo, idemRepo);

        var command = new AuthorizePaymentCommand("idem_timeout_pending", "party_timeout_user", 9900, "EUR");

        // Act
        var dto = await handler.Handle(command, CancellationToken.None);

        // Assert: payment must be in Pending state, never a silent decline
        dto.Should().NotBeNull();
        dto.State.Should().Be("Pending");
        dto.DeclineReason.Should().Contain("Acquirer connection timeout");

        paymentRepo.SavedPayments.Should().HaveCount(1);
        var payment = paymentRepo.SavedPayments[0];
        payment.State.Should().Be(PaymentState.Pending);
        payment.DeclineReason.Should().Contain("Acquirer connection timeout");

        // Verify Pending domain event emitted for reconciliation / outbox
        payment.DomainEvents.Should().ContainSingle();
        var domainEvt = payment.DomainEvents.First() as PaymentTransitionDomainEvent;
        domainEvt.Should().NotBeNull();
        domainEvt!.Outcome.Should().Be(PaymentLifecycleOutcome.Pending);
        domainEvt.PaymentId.Should().Be(payment.Id);

        // Verify idempotency record completes with 201 Created and cached Pending payment
        idemRepo.SavedRecords.Should().HaveCount(1);
        var record = idemRepo.SavedRecords[0];
        record.Status.Should().Be(IdempotencyStatus.Completed);
        record.ResponseStatusCode.Should().Be(201);
        record.ResponsePayload.Should().ContainEquivalentOf("\"state\":\"Pending\"");

        // Verify re-invocation with the same idempotency key returns cached Pending payment without second settlement attempt
        var replayedDto = await handler.Handle(command, CancellationToken.None);
        replayedDto.PaymentId.Should().Be(dto.PaymentId);
        replayedDto.State.Should().Be("Pending");
        mockSettlementPort.CallCount.Should().Be(1, "idempotent replay must not trigger a second settlement attempt");
    }

    private sealed class StubSettlementPort : ISettlementPort
    {
        public bool ReturnsUnanswered { get; set; }
        public string DeclineReason { get; set; } = "Timeout";
        public int CallCount { get; private set; }

        public Task<SettlementResult> AuthorizeAsync(string partyId, long amount, string currency, string? channel = null, CancellationToken ct = default)
            => AuthorizeAsync(partyId, amount, currency, channel, null, ct);

        public Task<SettlementResult> AuthorizeAsync(string partyId, long amount, string currency, string? channel, string? idempotencyKey, CancellationToken ct = default)
        {
            CallCount++;
            if (ReturnsUnanswered)
            {
                return Task.FromResult(SettlementResult.Unanswered(channel ?? "ADYEN", DeclineReason, idempotencyKey));
            }

            return Task.FromResult(SettlementResult.Success(channel ?? "ADYEN", "ref_auth_1", idempotencyKey));
        }

        public Task<SettlementResult> CaptureAsync(string paymentId, string channelReference, long amount, string currency, string? channel = null, CancellationToken ct = default)
            => Task.FromResult(SettlementResult.Success(channel ?? "ADYEN", "ref_cap", paymentId));

        public Task<SettlementResult> RefundAsync(string paymentId, string channelReference, long amount, string currency, string? channel = null, CancellationToken ct = default)
            => Task.FromResult(SettlementResult.Success(channel ?? "ADYEN", "ref_ref", paymentId));

        public Task<SettlementResult> CancelAsync(string paymentId, string channelReference, string? channel = null, CancellationToken ct = default)
            => Task.FromResult(SettlementResult.Success(channel ?? "ADYEN", "ref_cnc", paymentId));
    }

    private sealed class InMemoryPaymentRepo : IPaymentRepository
    {
        public List<Payment> SavedPayments { get; } = [];

        public Task AddAsync(Payment payment, CancellationToken ct = default)
        {
            SavedPayments.Add(payment);
            return Task.CompletedTask;
        }

        public Task<Payment?> GetByIdAsync(string id, CancellationToken ct = default)
            => Task.FromResult(SavedPayments.FirstOrDefault(p => p.Id == id));

        public Task<Payment?> GetByChannelReferenceAsync(string channelReference, CancellationToken ct = default)
            => Task.FromResult(SavedPayments.FirstOrDefault(p => p.ChannelReference == channelReference));

        public Task UpdateAsync(Payment payment, CancellationToken ct = default)
        {
            var idx = SavedPayments.FindIndex(p => p.Id == payment.Id);
            if (idx >= 0) SavedPayments[idx] = payment;
            else SavedPayments.Add(payment);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryIdemRepo : IIdempotencyRepository
    {
        public List<IdempotencyRecord> SavedRecords { get; } = [];

        public Task<IdempotencyRecord?> FindAsync(string key, string commandType, string? paymentId, CancellationToken ct)
            => Task.FromResult(SavedRecords.FirstOrDefault(r => r.Key == key && r.CommandType == commandType));

        public Task AddAsync(IdempotencyRecord record, CancellationToken ct)
        {
            SavedRecords.Add(record);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(IdempotencyRecord record, CancellationToken ct)
            => Task.CompletedTask;
    }
}
