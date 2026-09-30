using FluentAssertions;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using Xunit;

namespace PaymentGateway.UnitTests.Domain;

public class IdempotencyRecordTests
{
    [Fact]
    public void CreateInFlight_ShouldInitializeWithInFlightStatusAndLease()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var lease = TimeSpan.FromMinutes(2);
        var ttl = TimeSpan.FromHours(24);

        // Act
        var record = IdempotencyRecord.CreateInFlight(
            key: "idem_key_1",
            commandType: "Authorize",
            payloadHash: "hash_123",
            paymentId: null,
            leaseDuration: lease,
            ttl: ttl,
            now: now);

        // Assert
        record.Id.Should().NotBeEmpty();
        record.Key.Should().Be("idem_key_1");
        record.CommandType.Should().Be("Authorize");
        record.PaymentId.Should().BeNull();
        record.PayloadHash.Should().Be("hash_123");
        record.Status.Should().Be(IdempotencyStatus.InFlight);
        record.CreatedAt.Should().Be(now);
        record.ExpiresAt.Should().Be(now.Add(ttl));
        record.LockedUntil.Should().Be(now.Add(lease));
        record.IsLeaseActive(now.AddMinutes(1)).Should().BeTrue();
        record.IsLeaseActive(now.AddMinutes(3)).Should().BeFalse();
        record.IsExpired(now.AddHours(25)).Should().BeTrue();
    }

    [Fact]
    public void Complete_ShouldSetCompletedStatusAndResponse()
    {
        // Arrange
        var record = IdempotencyRecord.CreateInFlight("idem_key_1", "Authorize", "hash_123");

        // Act
        record.Complete(201, "{\"paymentId\":\"pay_1\"}");

        // Assert
        record.Status.Should().Be(IdempotencyStatus.Completed);
        record.ResponseStatusCode.Should().Be(201);
        record.ResponsePayload.Should().Be("{\"paymentId\":\"pay_1\"}");
        record.LockedUntil.Should().BeNull();
        record.IsLeaseActive(DateTimeOffset.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void Fail_ShouldSetFailedStatus()
    {
        // Arrange
        var record = IdempotencyRecord.CreateInFlight("idem_key_1", "Authorize", "hash_123");

        // Act
        record.Fail(500, "Internal Server Error");

        // Assert
        record.Status.Should().Be(IdempotencyStatus.Failed);
        record.ResponseStatusCode.Should().Be(500);
        record.ResponsePayload.Should().Be("Internal Server Error");
        record.LockedUntil.Should().BeNull();
    }

    [Fact]
    public void MatchesPayload_ShouldReturnTrueForMatchingHash()
    {
        // Arrange
        var record = IdempotencyRecord.CreateInFlight("idem_key_1", "Authorize", "hash_123");

        // Assert
        record.MatchesPayload("hash_123").Should().BeTrue();
        record.MatchesPayload("different_hash").Should().BeFalse();
    }
}
