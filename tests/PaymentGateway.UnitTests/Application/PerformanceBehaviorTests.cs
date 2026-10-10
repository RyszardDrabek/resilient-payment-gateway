using System.Diagnostics.Metrics;
using FluentAssertions;
using MediatR;
using PaymentGateway.Application.Behaviors;
using PaymentGateway.Application.Metrics;

namespace PaymentGateway.UnitTests.Application;

public sealed class PerformanceBehaviorTests
{
    private record TestRequest : IRequest<string>;

    [Fact]
    public async Task Handle_WhenSuccess_ReturnsResponse_AndRecordsMetric()
    {
        // Arrange
        using var listener = new MeterListener();
        double recordedDuration = -1;
        string? recordedOperation = null;
        string? recordedStatus = null;

        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == PaymentMetrics.MeterName && instrument.Name == "payment_handler_duration_seconds")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            recordedDuration = measurement;
            foreach (var tag in tags)
            {
                if (tag.Key == "operation") recordedOperation = tag.Value?.ToString();
                if (tag.Key == "status") recordedStatus = tag.Value?.ToString();
            }
        });

        listener.Start();

        var behavior = new PerformanceBehavior<TestRequest, string>();
        RequestHandlerDelegate<string> next = _ => Task.FromResult("ok");

        // Act
        var result = await behavior.Handle(new TestRequest(), next, CancellationToken.None);

        // Assert
        result.Should().Be("ok");
        recordedDuration.Should().BeGreaterThanOrEqualTo(0);
        recordedOperation.Should().Be(nameof(TestRequest));
        recordedStatus.Should().Be("success");
    }

    [Fact]
    public async Task Handle_WhenThrows_PropagatesException_AndRecordsFailedStatus()
    {
        // Arrange
        using var listener = new MeterListener();
        double recordedDuration = -1;
        string? recordedOperation = null;
        string? recordedStatus = null;

        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == PaymentMetrics.MeterName && instrument.Name == "payment_handler_duration_seconds")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            recordedDuration = measurement;
            foreach (var tag in tags)
            {
                if (tag.Key == "operation") recordedOperation = tag.Value?.ToString();
                if (tag.Key == "status") recordedStatus = tag.Value?.ToString();
            }
        });

        listener.Start();

        var behavior = new PerformanceBehavior<TestRequest, string>();
        RequestHandlerDelegate<string> next = _ => Task.FromException<string>(new InvalidOperationException("boom"));

        // Act
        var act = () => behavior.Handle(new TestRequest(), next, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        recordedDuration.Should().BeGreaterThanOrEqualTo(0);
        recordedOperation.Should().Be(nameof(TestRequest));
        recordedStatus.Should().Be("failed");
    }
}
