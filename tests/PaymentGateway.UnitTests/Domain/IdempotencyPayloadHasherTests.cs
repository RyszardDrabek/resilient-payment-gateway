using FluentAssertions;
using PaymentGateway.Domain.Services;
using Xunit;

namespace PaymentGateway.UnitTests.Domain;

public class IdempotencyPayloadHasherTests
{
    [Fact]
    public void ComputeHash_ShouldBeDeterministicAndDistinguishDifferences()
    {
        // Act
        var hash1 = IdempotencyPayloadHasher.ComputeHash("Authorize", null, "party_1", 1000, "EUR");
        var hash2 = IdempotencyPayloadHasher.ComputeHash("Authorize", null, "party_1", 1000, "EUR");
        var hashDifferentAmount = IdempotencyPayloadHasher.ComputeHash("Authorize", null, "party_1", 2000, "EUR");
        var hashDifferentParty = IdempotencyPayloadHasher.ComputeHash("Authorize", null, "party_2", 1000, "EUR");
        var hashDifferentCurrency = IdempotencyPayloadHasher.ComputeHash("Authorize", null, "party_1", 1000, "USD");
        var hashDifferentCommand = IdempotencyPayloadHasher.ComputeHash("Capture", null, "party_1", 1000, "EUR");
        var hashDifferentChannel = IdempotencyPayloadHasher.ComputeHash("Authorize", null, "party_1", 1000, "EUR", "ADYEN");

        // Assert
        hash1.Should().NotBeNullOrWhiteSpace();
        hash1.Should().Be(hash2);
        hash1.Should().NotBe(hashDifferentAmount);
        hash1.Should().NotBe(hashDifferentParty);
        hash1.Should().NotBe(hashDifferentCurrency);
        hash1.Should().NotBe(hashDifferentCommand);
        hash1.Should().NotBe(hashDifferentChannel);
    }
}
