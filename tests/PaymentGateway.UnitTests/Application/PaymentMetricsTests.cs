using System.Diagnostics.Metrics;
using PaymentGateway.Application.Metrics;
using Xunit;

namespace PaymentGateway.UnitTests.Application;

public sealed class PaymentMetricsTests
{
    [Fact]
    public void RecordVolume_PublishesMeasurementWithTags()
    {
        // Arrange
        long recordedValue = 0;
        string? recordedCurrency = null;
        string? recordedChannel = null;
        string? recordedOp = null;
        string? recordedStatus = null;

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == PaymentMetrics.MeterName && instrument.Name == "payment_volume_total")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "payment_volume_total" && measurement == 998877)
            {
                recordedValue = measurement;
                foreach (var tag in tags)
                {
                    if (tag.Key == "currency") recordedCurrency = tag.Value?.ToString();
                    if (tag.Key == "channel") recordedChannel = tag.Value?.ToString();
                    if (tag.Key == "operation") recordedOp = tag.Value?.ToString();
                    if (tag.Key == "status") recordedStatus = tag.Value?.ToString();
                }
            }
        });

        listener.Start();

        // Act
        PaymentMetrics.RecordVolume(998877, "EUR", "ADYEN", "test_authorize", "Authorized");

        // Assert
        Assert.Equal(998877, recordedValue);
        Assert.Equal("EUR", recordedCurrency);
        Assert.Equal("ADYEN", recordedChannel);
        Assert.Equal("test_authorize", recordedOp);
        Assert.Equal("Authorized", recordedStatus);
    }

    [Fact]
    public void RecordChannelDuration_PublishesHistogramMeasurementWithTags()
    {
        // Arrange
        double recordedDuration = 0;
        string? recordedChannel = null;
        string? recordedOp = null;
        string? recordedStatus = null;

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == PaymentMetrics.MeterName && instrument.Name == "payment_channel_duration_seconds")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "payment_channel_duration_seconds")
            {
                string? channel = null;
                foreach (var tag in tags)
                {
                    if (tag.Key == "channel") channel = tag.Value?.ToString();
                }

                if (channel == "TEST_WEB3")
                {
                    recordedDuration = measurement;
                    recordedChannel = channel;
                    foreach (var tag in tags)
                    {
                        if (tag.Key == "operation") recordedOp = tag.Value?.ToString();
                        if (tag.Key == "status") recordedStatus = tag.Value?.ToString();
                    }
                }
            }
        });

        listener.Start();

        // Act
        PaymentMetrics.RecordChannelDuration(0.125, "TEST_WEB3", "authorize", "Pending");

        // Assert
        Assert.True(recordedDuration > 0.1);
        Assert.Equal("TEST_WEB3", recordedChannel);
        Assert.Equal("authorize", recordedOp);
        Assert.Equal("Pending", recordedStatus);
    }

    [Fact]
    public void RecordWebhookDispatch_PublishesCounterMeasurementWithTags()
    {
        // Arrange
        long recordedValue = 0;
        string? recordedStatus = null;
        string? recordedCode = null;

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == PaymentMetrics.MeterName && instrument.Name == "merchant_webhook_dispatched_total")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "merchant_webhook_dispatched_total")
            {
                string? code = null;
                foreach (var tag in tags)
                {
                    if (tag.Key == "status_code") code = tag.Value?.ToString();
                }

                if (code == "999")
                {
                    recordedValue = measurement;
                    recordedCode = code;
                    foreach (var tag in tags)
                    {
                        if (tag.Key == "status") recordedStatus = tag.Value?.ToString();
                    }
                }
            }
        });

        listener.Start();

        // Act
        PaymentMetrics.RecordWebhookDispatch("delivered", 999);

        // Assert
        Assert.Equal(1, recordedValue);
        Assert.Equal("delivered", recordedStatus);
        Assert.Equal("999", recordedCode);
    }

    [Fact]
    public void RecordWebhookReplay_PublishesCounterMeasurementWithTags()
    {
        // Arrange
        long recordedValue = 0;
        string? recordedOutcome = null;
        string? recordedCode = null;

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == PaymentMetrics.MeterName && instrument.Name == "merchant_webhook_replay_attempts_total")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "merchant_webhook_replay_attempts_total")
            {
                string? code = null;
                foreach (var tag in tags)
                {
                    if (tag.Key == "status_code") code = tag.Value?.ToString();
                }

                if (code == "998")
                {
                    recordedValue = measurement;
                    recordedCode = code;
                    foreach (var tag in tags)
                    {
                        if (tag.Key == "outcome") recordedOutcome = tag.Value?.ToString();
                    }
                }
            }
        });

        listener.Start();

        // Act
        PaymentMetrics.RecordWebhookReplay("failure", 998);

        // Assert
        Assert.Equal(1, recordedValue);
        Assert.Equal("failure", recordedOutcome);
        Assert.Equal("998", recordedCode);
    }

    [Fact]
    public void RecordWebhookDuration_PublishesHistogramMeasurementWithTags()
    {
        // Arrange
        double recordedDuration = 0;
        string? recordedStatus = null;

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == PaymentMetrics.MeterName && instrument.Name == "merchant_webhook_dispatch_duration_seconds")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "merchant_webhook_dispatch_duration_seconds")
            {
                string? status = null;
                foreach (var tag in tags)
                {
                    if (tag.Key == "status") status = tag.Value?.ToString();
                }

                if (status == "test_isolated_status")
                {
                    recordedDuration = measurement;
                    recordedStatus = status;
                }
            }
        });

        listener.Start();

        // Act
        PaymentMetrics.RecordWebhookDuration(0.045, "test_isolated_status");

        // Assert
        Assert.True(recordedDuration > 0.04);
        Assert.Equal("test_isolated_status", recordedStatus);
    }

    [Fact]
    public void RecordOutboxDispatch_PublishesCounterMeasurement()
    {
        long recordedValue = 0;
        string? recordedStatus = null;

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == PaymentMetrics.MeterName && instrument.Name == "outbox_messages_dispatched_total")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "outbox_messages_dispatched_total")
            {
                foreach (var tag in tags)
                {
                    if (tag.Key == "status" && tag.Value?.ToString() == "test_outbox_success")
                    {
                        recordedValue = measurement;
                        recordedStatus = "test_outbox_success";
                    }
                }
            }
        });

        listener.Start();

        PaymentMetrics.RecordOutboxDispatch("test_outbox_success");

        Assert.Equal(1, recordedValue);
        Assert.Equal("test_outbox_success", recordedStatus);
    }

    [Fact]
    public void RecordOutboxDeadLettered_PublishesCounterMeasurement()
    {
        long recordedValue = 0;

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == PaymentMetrics.MeterName && instrument.Name == "outbox_messages_dead_lettered_total")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "outbox_messages_dead_lettered_total")
            {
                recordedValue += measurement;
            }
        });

        listener.Start();

        PaymentMetrics.RecordOutboxDeadLettered();

        Assert.True(recordedValue >= 1);
    }

    [Fact]
    public void RecordOutboxDuration_PublishesHistogramMeasurement()
    {
        double recordedDuration = 0;

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == PaymentMetrics.MeterName && instrument.Name == "outbox_dispatch_duration_seconds")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "outbox_dispatch_duration_seconds" && measurement == 0.1234)
            {
                recordedDuration = measurement;
            }
        });

        listener.Start();

        PaymentMetrics.RecordOutboxDuration(0.1234);

        Assert.Equal(0.1234, recordedDuration);
    }
}

