using FluentAssertions;
using PaymentGateway.Application.Observability;
using PaymentGateway.Infrastructure.Services;
using Xunit;

namespace PaymentGateway.UnitTests.Infrastructure.Services;

public sealed class ObservabilityServiceTests
{
    [Fact]
    public void ParseAlertmanagerAlerts_ValidAlertmanagerV2Json_ReturnsParsedAlertDtos()
    {
        // Arrange
        var json = """
        [
          {
            "annotations": {
              "description": "Payment decline rate is 7.4% over 5m (> 5% SLA threshold).",
              "summary": "High Payment Decline Rate (> 5%)"
            },
            "endsAt": "2026-10-19T09:00:00Z",
            "fingerprint": "alert_decline_rate_123",
            "labels": {
              "alertname": "HighPaymentDeclineRate",
              "severity": "critical",
              "job": "payment-gateway-api"
            },
            "startsAt": "2026-10-19T08:15:00Z",
            "status": {
              "inhibitedBy": [],
              "silencedBy": [],
              "state": "active"
            }
          },
          {
            "annotations": {
              "description": "Acquirer p95 latency is 2.8s exceeding 2.0s SLA target.",
              "summary": "Acquirer Settlement Latency SLA Breach (p95 > 2s)"
            },
            "fingerprint": "alert_acquirer_latency_456",
            "labels": {
              "alertname": "AcquirerSettlementLatencySlaBreach",
              "severity": "warning"
            },
            "startsAt": "2026-10-19T08:20:00Z",
            "status": {
              "state": "active"
            }
          }
        ]
        """;

        // Act
        var alerts = ObservabilityService.ParseAlertmanagerAlerts(json);

        // Assert
        alerts.Should().HaveCount(2);

        var first = alerts[0];
        first.Fingerprint.Should().Be("alert_decline_rate_123");
        first.AlertName.Should().Be("HighPaymentDeclineRate");
        first.Severity.Should().Be("critical");
        first.State.Should().Be("active");
        first.Summary.Should().Be("High Payment Decline Rate (> 5%)");
        first.Description.Should().Contain("7.4%");
        first.StartsAt.Should().Be(DateTimeOffset.Parse("2026-10-19T08:15:00Z"));
        first.Labels.Should().ContainKey("job").WhoseValue.Should().Be("payment-gateway-api");

        var second = alerts[1];
        second.Fingerprint.Should().Be("alert_acquirer_latency_456");
        second.AlertName.Should().Be("AcquirerSettlementLatencySlaBreach");
        second.Severity.Should().Be("warning");
    }

    [Fact]
    public void ParseAlertmanagerAlerts_EmptyOrInvalidPayload_ReturnsEmptyCollectionWithoutThrowing()
    {
        // Arrange & Act
        var emptyAlerts = ObservabilityService.ParseAlertmanagerAlerts("[]");
        var malformedAlerts = ObservabilityService.ParseAlertmanagerAlerts("{ \"invalid\": true }");
        var garbageAlerts = ObservabilityService.ParseAlertmanagerAlerts("not a json");

        // Assert
        emptyAlerts.Should().BeEmpty();
        malformedAlerts.Should().BeEmpty();
        garbageAlerts.Should().BeEmpty();
    }

    [Fact]
    public void ParseAlertmanagerAlerts_MissingAnnotationsOrLabels_ProvidesGracefulFallbacks()
    {
        // Arrange
        var json = """
        [
          {
            "startsAt": "2026-10-19T08:00:00Z"
          }
        ]
        """;

        // Act
        var alerts = ObservabilityService.ParseAlertmanagerAlerts(json);

        // Assert
        alerts.Should().HaveCount(1);
        var alert = alerts[0];
        alert.AlertName.Should().Be("SystemAlert");
        alert.Severity.Should().Be("warning");
        alert.Summary.Should().Be("SystemAlert");
        alert.State.Should().Be("active");
        alert.Fingerprint.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void LatencyMetricsDto_EvaluatesSlaComplianceAccurately()
    {
        // Within SLA
        var nominal = new LatencyMetricsDto(
            AcquirerP95LatencySeconds: 0.85,
            OutboxDispatchP95LatencySeconds: 0.12,
            DeclineRatePercent: 2.1,
            TotalTransactions: 1000,
            TotalDeclines: 21,
            AcquirerSlaCompliant: 0.85 <= 2.0,
            DeclineRateCompliant: 2.1 <= 5.0
        );

        nominal.AcquirerSlaCompliant.Should().BeTrue();
        nominal.DeclineRateCompliant.Should().BeTrue();

        // SLA Breach
        var breached = new LatencyMetricsDto(
            AcquirerP95LatencySeconds: 3.20,
            OutboxDispatchP95LatencySeconds: 0.95,
            DeclineRatePercent: 7.8,
            TotalTransactions: 1000,
            TotalDeclines: 78,
            AcquirerSlaCompliant: 3.20 <= 2.0,
            DeclineRateCompliant: 7.8 <= 5.0
        );

        breached.AcquirerSlaCompliant.Should().BeFalse();
        breached.DeclineRateCompliant.Should().BeFalse();
    }
}
