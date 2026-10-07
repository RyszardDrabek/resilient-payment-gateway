using System.Diagnostics;
using System.Runtime.CompilerServices;
using FluentAssertions;
using OpenTelemetry.Logs;
using PaymentGateway.Edge.Telemetry;
using Xunit;

namespace PaymentGateway.UnitTests.Edge;

/// <summary>
/// Unit tests for <see cref="InstrumentMaskingProcessor"/> and <see cref="LogInstrumentMaskingProcessor"/>.
/// Activities and LogRecords are tested directly to verify masking across traces and logs (F-EDGE-03).
/// </summary>
public sealed class InstrumentMaskingProcessorTests
{
    private readonly InstrumentMaskingProcessor _activitySut = new();
    private readonly LogInstrumentMaskingProcessor _logSut = new();

    // ── helpers ──────────────────────────────────────────────────────────────

    private static Activity CreateActivity(string tagKey, object tagValue)
    {
        var activity = new Activity("TestSpan");
        activity.Start();
        activity.SetTag(tagKey, tagValue);
        return activity;
    }

    private static LogRecord CreateLogRecord(string? formattedMessage = null, string? body = null, List<KeyValuePair<string, object?>>? attributes = null)
    {
        var record = (LogRecord)RuntimeHelpers.GetUninitializedObject(typeof(LogRecord));
        if (formattedMessage is not null)
        {
            record.FormattedMessage = formattedMessage;
        }

        if (body is not null)
        {
            record.Body = body;
        }

        if (attributes is not null)
        {
            record.Attributes = attributes;
        }

        return record;
    }

    // ── AC-1 / AC-2: PAN masking (13–19 digits, Amex 4-6-5, Maestro/UnionPay) ────

    [Fact]
    public void InstrumentMaskingProcessor_MasksPan_InSpanAttribute()
    {
        using var activity = CreateActivity("payment.instrument", "4111111111111111");

        _activitySut.OnEnd(activity);

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

        _activitySut.OnEnd(activity);

        var value = activity.GetTagItem("payment.instrument") as string;
        value.Should().NotBeNull();
        value.Should().NotContain("4111 1111 1111 1111");
        value.Should().Contain("****1111");
    }

    [Fact]
    public void InstrumentMaskingProcessor_MasksAmexFormatPan_InSpanAttribute()
    {
        // 15-digit Amex formatted 4-6-5
        using var activity = CreateActivity("payment.instrument", "3782 822463 10005");

        _activitySut.OnEnd(activity);

        var value = activity.GetTagItem("payment.instrument") as string;
        value.Should().NotBeNull();
        value.Should().NotContain("3782 822463 10005");
        value.Should().Be("****0005");
    }

    [Fact]
    public void InstrumentMaskingProcessor_MasksNineteenDigitPan_InSpanAttribute()
    {
        // 19-digit Maestro / UnionPay
        using var activity = CreateActivity("payment.instrument", "1234567890123456789");

        _activitySut.OnEnd(activity);

        var value = activity.GetTagItem("payment.instrument") as string;
        value.Should().NotBeNull();
        value.Should().NotContain("1234567890123456789");
        value.Should().Be("****6789");
    }

    // ── AC-1 / AC-2: Token (32-char hex) ────────────────────────────────────

    [Fact]
    public void InstrumentMaskingProcessor_MasksToken_InSpanAttribute()
    {
        using var activity = CreateActivity("payment.token", "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4");

        _activitySut.OnEnd(activity);

        var value = activity.GetTagItem("payment.token") as string;
        value.Should().NotBeNull();
        value.Should().NotContain("a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4");
        value.Should().Contain("****");
        value.Should().EndWith("c3d4");
    }

    // ── AC-1 / AC-2: IBAN masking (electronic and spaced) ───────────────────

    [Fact]
    public void InstrumentMaskingProcessor_MasksIban_InSpanAttribute()
    {
        using var activity = CreateActivity("payment.account", "DE89370400440532013000");

        _activitySut.OnEnd(activity);

        var value = activity.GetTagItem("payment.account") as string;
        value.Should().NotBeNull();
        value.Should().NotContain("DE89370400440532013000");
        value.Should().Be("****3000");
    }

    [Fact]
    public void InstrumentMaskingProcessor_MasksSpacedIban_InSpanAttribute()
    {
        using var activity = CreateActivity("payment.account", "DE89 3704 0044 0532 0130 00");

        _activitySut.OnEnd(activity);

        var value = activity.GetTagItem("payment.account") as string;
        value.Should().NotBeNull();
        value.Should().NotContain("DE89 3704 0044 0532 0130 00");
        value.Should().Be("****3000");
    }

    // ── AC-2: irreversibility ────────────────────────────────────────────────

    [Fact]
    public void InstrumentMaskingProcessor_ReplacedValue_IsNotTheOriginal()
    {
        const string pan = "5500005555555559";
        using var activity = CreateActivity("card", pan);

        _activitySut.OnEnd(activity);

        var value = activity.GetTagItem("card") as string;
        value.Should().NotBe(pan);
    }

    // ── partial masking (PAN embedded in sentence) ────────────────────────────

    [Fact]
    public void InstrumentMaskingProcessor_PartiallyMasksAttribute_ContainingPanAndSafeText()
    {
        using var activity = CreateActivity("detail", "payment ref=4111111111111111 ok");

        _activitySut.OnEnd(activity);

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

        _activitySut.OnEnd(activity);

        var value = activity.GetTagItem("http.method") as string;
        value.Should().Be("POST");
    }

    [Fact]
    public void InstrumentMaskingProcessor_DoesNotThrow_WhenAttributeIsEmpty()
    {
        using var activity = CreateActivity("payment.instrument", string.Empty);

        var act = () => _activitySut.OnEnd(activity);

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

        _activitySut.OnEnd(activity);

        var value = activity.GetTagItem("http.status_code");
        value.Should().Be(200);
    }

    // ── AC-1: Log masking (LogInstrumentMaskingProcessor) ─────────────────────

    [Fact]
    public void LogInstrumentMaskingProcessor_MasksPan_InFormattedMessage()
    {
        var record = CreateLogRecord(formattedMessage: "Processing card 4111 1111 1111 1111 for user");

        _logSut.OnEnd(record);

        record.FormattedMessage.Should().NotContain("4111 1111 1111 1111");
        record.FormattedMessage.Should().Contain("****1111");
    }

    [Fact]
    public void LogInstrumentMaskingProcessor_MasksBody_WhenPresent()
    {
        var record = CreateLogRecord(body: "Card payload: 3782 822463 10005");

        _logSut.OnEnd(record);

        record.Body.Should().NotContain("3782 822463 10005");
        record.Body.Should().Contain("****0005");
    }

    [Fact]
    public void LogInstrumentMaskingProcessor_MasksStructuredAttributes()
    {
        var attributes = new List<KeyValuePair<string, object?>>
        {
            new("user.id", "usr_123"),
            new("payment.instrument", "DE89 3704 0044 0532 0130 00"),
            new("payment.token", "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4")
        };
        var record = CreateLogRecord(attributes: attributes);

        _logSut.OnEnd(record);

        record.Attributes.Should().NotBeNull();
        record.Attributes!.First(k => k.Key == "user.id").Value.Should().Be("usr_123");
        record.Attributes!.First(k => k.Key == "payment.instrument").Value.Should().Be("****3000");
        record.Attributes!.First(k => k.Key == "payment.token").Value.Should().Be("****c3d4");
    }

    [Fact]
    public void LogInstrumentMaskingProcessor_SafeLogRecord_IsUnchanged()
    {
        var attributes = new List<KeyValuePair<string, object?>>
        {
            new("http.route", "/payments"),
            new("status", 200)
        };
        var record = CreateLogRecord(formattedMessage: "Payment succeeded", attributes: attributes);

        _logSut.OnEnd(record);

        record.FormattedMessage.Should().Be("Payment succeeded");
        record.Attributes!.First(k => k.Key == "http.route").Value.Should().Be("/payments");
        record.Attributes!.First(k => k.Key == "status").Value.Should().Be(200);
    }

    [Fact]
    public void LogInstrumentMaskingProcessor_DoesNotThrow_OnNullOrEmpty()
    {
        var act = () => _logSut.OnEnd(null!);
        act.Should().NotThrow();

        var record = CreateLogRecord();
        act = () => _logSut.OnEnd(record);
        act.Should().NotThrow();
    }
}
