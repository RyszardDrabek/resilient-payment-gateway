using System.Diagnostics;
using FluentAssertions;
using PaymentGateway.Edge.Telemetry;
using Xunit;

namespace PaymentGateway.UnitTests.Edge;

/// <summary>
/// Unit tests for <see cref="InstrumentMaskingProcessor"/>.
/// Activities are constructed directly (not via ActivitySource) to avoid
/// listener registration ordering issues in the xUnit test runner.
/// </summary>
public sealed class InstrumentMaskingProcessorTests
{
    private readonly InstrumentMaskingProcessor _sut = new();

    // ── helpers ──────────────────────────────────────────────────────────────

    private static Activity CreateActivity(string tagKey, object tagValue)
    {
        // Construct an Activity directly; it is always non-null and always recording.
        var activity = new Activity("TestSpan");
        activity.Start();
        activity.SetTag(tagKey, tagValue);
        return activity;
    }

    // ── AC-1 / AC-2: PAN masking ─────────────────────────────────────────────

    [Fact]
    public void InstrumentMaskingProcessor_MasksPan_InSpanAttribute()
    {
        using var activity = CreateActivity("payment.instrument", "4111111111111111");

        _sut.OnEnd(activity);

        var value = activity.GetTagItem("payment.instrument") as string;
        value.Should().NotBeNull();
        value.Should().NotContain("4111111111111111");
        value.Should().Contain("****");
        value.Should().EndWith("1111");
    }

    [Fact]
    public void InstrumentMaskingProcessor_MasksSpacedPan_InSpanAttribute()
    {
        using var activity = CreateActivity("payment.instrument", "4111 1111 1111 1111");

        _sut.OnEnd(activity);

        var value = activity.GetTagItem("payment.instrument") as string;
        value.Should().NotBeNull();
        value.Should().NotContain("4111 1111 1111 1111");
        value.Should().Contain("****");
    }

    // ── AC-1 / AC-2: Token (32-char hex) ────────────────────────────────────

    [Fact]
    public void InstrumentMaskingProcessor_MasksToken_InSpanAttribute()
    {
        using var activity = CreateActivity("payment.token", "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4");

        _sut.OnEnd(activity);

        var value = activity.GetTagItem("payment.token") as string;
        value.Should().NotBeNull();
        value.Should().NotContain("a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4");
        value.Should().Contain("****");
        value.Should().EndWith("c3d4");
    }

    // ── AC-1 / AC-2: IBAN masking ────────────────────────────────────────────

    [Fact]
    public void InstrumentMaskingProcessor_MasksIban_InSpanAttribute()
    {
        using var activity = CreateActivity("payment.account", "DE89370400440532013000");

        _sut.OnEnd(activity);

        var value = activity.GetTagItem("payment.account") as string;
        value.Should().NotBeNull();
        value.Should().NotContain("DE89370400440532013000");
        value.Should().Contain("****");
    }

    // ── AC-2: irreversibility ────────────────────────────────────────────────

    [Fact]
    public void InstrumentMaskingProcessor_ReplacedValue_IsNotTheOriginal()
    {
        const string pan = "5500005555555559";
        using var activity = CreateActivity("card", pan);

        _sut.OnEnd(activity);

        var value = activity.GetTagItem("card") as string;
        value.Should().NotBe(pan);
    }

    // ── partial masking (PAN embedded in sentence) ────────────────────────────

    [Fact]
    public void InstrumentMaskingProcessor_PartiallyMasksAttribute_ContainingPanAndSafeText()
    {
        using var activity = CreateActivity("detail", "payment ref=4111111111111111 ok");

        _sut.OnEnd(activity);

        var value = activity.GetTagItem("detail") as string;
        value.Should().NotBeNull();
        value.Should().NotContain("4111111111111111");
        value.Should().Contain("****1111");
        value.Should().Contain("payment ref=");
        value.Should().Contain("ok");
    }

    // ── safe attributes are untouched ─────────────────────────────────────────

    [Fact]
    public void InstrumentMaskingProcessor_SafeAttribute_IsUnchanged()
    {
        using var activity = CreateActivity("http.method", "POST");

        _sut.OnEnd(activity);

        var value = activity.GetTagItem("http.method") as string;
        value.Should().Be("POST");
    }

    [Fact]
    public void InstrumentMaskingProcessor_DoesNotThrow_WhenAttributeIsEmpty()
    {
        using var activity = CreateActivity("payment.instrument", string.Empty);

        var act = () => _sut.OnEnd(activity);

        act.Should().NotThrow();
        var value = activity.GetTagItem("payment.instrument") as string;
        value.Should().BeEmpty();
    }

    // ── non-string attributes survive without corruption ──────────────────────

    [Fact]
    public void InstrumentMaskingProcessor_NonStringAttribute_IsUnchanged()
    {
        using var activity = new Activity("NumericTagSpan");
        activity.Start();
        activity.SetTag("http.status_code", 200);

        _sut.OnEnd(activity);

        var value = activity.GetTagItem("http.status_code");
        value.Should().Be(200);
    }
}
