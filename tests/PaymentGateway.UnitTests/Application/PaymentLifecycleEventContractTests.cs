using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using PaymentGateway.Application.Events;

namespace PaymentGateway.UnitTests.Application;

public sealed class PaymentLifecycleEventContractTests
{
    [Fact]
    public void PaymentLifecycleEvent_DoesNotContainRawInstrumentProperties()
    {
        var properties = typeof(PaymentLifecycleEvent).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var propertyNames = properties.Select(p => p.Name.ToLowerInvariant()).ToList();

        propertyNames.Should().NotContain("pan");
        propertyNames.Should().NotContain("cardnumber");
        propertyNames.Should().NotContain("token");
        propertyNames.Should().NotContain("paymenttoken");
        propertyNames.Should().NotContain("iban");
        propertyNames.Should().NotContain("cvv");
        propertyNames.Should().NotContain("expiry");
    }

    [Fact]
    public void PaymentLifecycleEvent_SerializesAndDeserializesCorrectly()
    {
        var now = DateTimeOffset.UtcNow;
        var original = new PaymentLifecycleEvent(
            EventId: "evt_12345",
            PaymentId: "pay_98765",
            PartyId: "party_cust_111",
            Outcome: "authorized",
            Amount: 1500L,
            Currency: "EUR",
            Version: 1,
            OccurredAt: now);

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<PaymentLifecycleEvent>(json);

        deserialized.Should().NotBeNull();
        deserialized!.EventId.Should().Be(original.EventId);
        deserialized.PaymentId.Should().Be(original.PaymentId);
        deserialized.PartyId.Should().Be(original.PartyId);
        deserialized.Outcome.Should().Be(original.Outcome);
        deserialized.Amount.Should().Be(original.Amount);
        deserialized.Currency.Should().Be(original.Currency);
        deserialized.Version.Should().Be(original.Version);
        deserialized.OccurredAt.Should().Be(original.OccurredAt);
    }
}
