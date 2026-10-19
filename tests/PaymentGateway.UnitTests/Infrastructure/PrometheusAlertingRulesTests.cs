using FluentAssertions;
using Xunit;

namespace PaymentGateway.UnitTests.Infrastructure;

public sealed class PrometheusAlertingRulesTests
{
    private static string FindRepoRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "PaymentGateway.slnx")) ||
                Directory.Exists(Path.Combine(current, "compose")))
            {
                return current;
            }

            var parent = Directory.GetParent(current);
            if (parent is null) break;
            current = parent.FullName;
        }

        return AppContext.BaseDirectory;
    }

    [Fact]
    public void AlertRulesFile_ExistsAndDefinesRequiredPromQlAlertRules()
    {
        var repoRoot = FindRepoRoot();
        var alertRulesPath = Path.Combine(repoRoot, "compose", "alert.rules.yml");

        File.Exists(alertRulesPath).Should().BeTrue($"Expected alert rules file at {alertRulesPath}");
        var content = File.ReadAllText(alertRulesPath);

        // Rule 1: High payment decline rate (> 5%)
        content.Should().Contain("HighPaymentDeclineRate");
        content.Should().Contain("payment_declines_total");
        content.Should().Contain("payment_transactions_total");
        content.Should().Contain("> 5");

        // Rule 2: Acquirer latency SLA breach (p95 > 2s)
        content.Should().Contain("AcquirerSettlementLatencySlaBreach");
        content.Should().Contain("payment_channel_duration_seconds_bucket");
        content.Should().Contain("0.95");
        content.Should().Contain("> 2.0");

        // Rule 3: Merchant webhook DLQ exhaustion or replay spike
        content.Should().Contain("MerchantWebhookDlqSpike");
        content.Should().Contain("merchant_webhook_dispatched_total");
        content.Should().Contain("merchant_webhook_replay_attempts_total");

        // Rule 4: Outbox delivery delay or failure
        content.Should().Contain("OutboxDeliveryDelayOrFailure");
        content.Should().Contain("outbox_messages_dispatched_total");
        content.Should().Contain("outbox_messages_dead_lettered_total");
    }

    [Fact]
    public void AlertmanagerConfigFile_ExistsAndDefinesRouteAndReceivers()
    {
        var repoRoot = FindRepoRoot();
        var alertmanagerPath = Path.Combine(repoRoot, "compose", "alertmanager.yml");

        File.Exists(alertmanagerPath).Should().BeTrue($"Expected alertmanager config at {alertmanagerPath}");
        var content = File.ReadAllText(alertmanagerPath);

        content.Should().Contain("route:");
        content.Should().Contain("receivers:");
        content.Should().Contain("default-receiver");
    }

    [Fact]
    public void PrometheusConfigFile_WiresAlertmanagerAndRulesFile()
    {
        var repoRoot = FindRepoRoot();
        var prometheusPath = Path.Combine(repoRoot, "compose", "prometheus.yml");

        File.Exists(prometheusPath).Should().BeTrue($"Expected prometheus config at {prometheusPath}");
        var content = File.ReadAllText(prometheusPath);

        content.Should().Contain("alerting:");
        content.Should().Contain("alertmanager:9093");
        content.Should().Contain("/etc/prometheus/alert.rules.yml");
        content.Should().Contain("job_name: alertmanager");
    }

    [Fact]
    public void DockerCompose_DefinesAlertmanagerServiceAndMountsAlertRules()
    {
        var repoRoot = FindRepoRoot();
        var dockerComposePath = Path.Combine(repoRoot, "compose", "docker-compose.yml");

        File.Exists(dockerComposePath).Should().BeTrue($"Expected docker-compose.yml at {dockerComposePath}");
        var content = File.ReadAllText(dockerComposePath);

        content.Should().Contain("alertmanager:");
        content.Should().Contain("prom/alertmanager");
        content.Should().Contain("9093:9093");
        content.Should().Contain("./alert.rules.yml:/etc/prometheus/alert.rules.yml:ro");
    }

    [Fact]
    public void GrafanaDatasources_ConfiguresAlertmanagerDatasource()
    {
        var repoRoot = FindRepoRoot();
        var datasourcesPath = Path.Combine(repoRoot, "compose", "grafana-datasources.yml");

        File.Exists(datasourcesPath).Should().BeTrue($"Expected grafana-datasources.yml at {datasourcesPath}");
        var content = File.ReadAllText(datasourcesPath);

        content.Should().Contain("name: Alertmanager");
        content.Should().Contain("type: alertmanager");
        content.Should().Contain("http://alertmanager:9093");
    }
}
