using PaymentGateway.Domain.Entities;
using Xunit;

namespace PaymentGateway.UnitTests.Domain;

public sealed class MerchantWebhookDeadLetterTests
{
    [Fact]
    public void Create_WithValidParameters_SetsPropertiesAndExhaustedStatus()
    {
        // Act
        var dlq = MerchantWebhookDeadLetter.Create(
            eventId: "evt_123",
            paymentId: "pay_456",
            merchantId: "merch_acme",
            destinationUrl: "https://example.com/webhook",
            payloadJson: "{\"amount\":1000}",
            deliveryAttempts: 5,
            lastErrorMessage: "HTTP 500 Internal Server Error",
            lastStatusCode: 500);

        // Assert
        Assert.NotEqual(Guid.Empty, dlq.Id);
        Assert.Equal("evt_123", dlq.EventId);
        Assert.Equal("pay_456", dlq.PaymentId);
        Assert.Equal("merch_acme", dlq.MerchantId);
        Assert.Equal("https://example.com/webhook", dlq.DestinationUrl);
        Assert.Equal("{\"amount\":1000}", dlq.PayloadJson);
        Assert.Equal(5, dlq.DeliveryAttempts);
        Assert.Equal("HTTP 500 Internal Server Error", dlq.LastErrorMessage);
        Assert.Equal(500, dlq.LastStatusCode);
        Assert.Equal("exhausted", dlq.Status);
        Assert.True(dlq.FailedAt <= DateTimeOffset.UtcNow);
        Assert.Null(dlq.ReplayedAt);
    }

    [Theory]
    [InlineData("", "pay_456", "merch_acme", "https://example.com")]
    [InlineData("evt_123", "", "merch_acme", "https://example.com")]
    [InlineData("evt_123", "pay_456", "", "https://example.com")]
    [InlineData("evt_123", "pay_456", "merch_acme", "")]
    public void Create_MissingRequiredFields_ThrowsArgumentException(
        string eventId,
        string paymentId,
        string merchantId,
        string destinationUrl)
    {
        Assert.Throws<ArgumentException>(() =>
            MerchantWebhookDeadLetter.Create(
                eventId,
                paymentId,
                merchantId,
                destinationUrl,
                "{}",
                1,
                "Error",
                500));
    }

    [Fact]
    public void MarkReplayed_UpdatesStatusAndTimestamp()
    {
        // Arrange
        var dlq = MerchantWebhookDeadLetter.Create(
            "evt_123",
            "pay_456",
            "merch_acme",
            "https://example.com/webhook",
            "{}",
            5,
            "Timeout",
            null);

        // Act
        dlq.MarkReplayed();

        // Assert
        Assert.Equal("replayed", dlq.Status);
        Assert.NotNull(dlq.ReplayedAt);
        Assert.True(dlq.ReplayedAt <= DateTimeOffset.UtcNow);
        Assert.Equal(1, dlq.ReplayAttemptCount);
        Assert.Equal(200, dlq.LastReplayStatusCode);
    }

    [Fact]
    public void RecordReplayAttempt_WhenFailed_IncrementsCountAndRecordsStatusCodeWithoutChangingStatus()
    {
        // Arrange
        var dlq = MerchantWebhookDeadLetter.Create(
            "evt_123",
            "pay_456",
            "merch_acme",
            "https://example.com/webhook",
            "{}",
            5,
            "Timeout",
            null);

        // Act
        dlq.RecordReplayAttempt(successful: false, statusCode: 502);

        // Assert
        Assert.Equal("exhausted", dlq.Status);
        Assert.Null(dlq.ReplayedAt);
        Assert.Equal(1, dlq.ReplayAttemptCount);
        Assert.Equal(502, dlq.LastReplayStatusCode);
    }
}
