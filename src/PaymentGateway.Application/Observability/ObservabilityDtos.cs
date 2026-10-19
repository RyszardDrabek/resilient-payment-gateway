namespace PaymentGateway.Application.Observability;

public sealed record ObservabilitySummaryDto(
    DateTimeOffset Timestamp,
    ServiceHealthDto Api,
    DatabaseStatusDto Database,
    BrokerStatusDto MessageBroker,
    MonitoringServicesStatusDto Monitoring,
    IReadOnlyList<AlertmanagerAlertDto> ActiveAlerts,
    LatencyMetricsDto LatencyMetrics,
    QueueHealthDto Queues
);

public sealed record ServiceHealthDto(
    string Status,
    string ServiceName,
    string Version,
    string Environment,
    TimeSpan Uptime
);

public sealed record DatabaseStatusDto(
    string Status,
    bool PaymentDbConnected,
    bool RiskDbConnected,
    double ResponseTimeMs
);

public sealed record BrokerStatusDto(
    string Status,
    string Provider,
    string Host
);

public sealed record MonitoringServicesStatusDto(
    string PrometheusStatus,
    string AlertmanagerStatus
);

public sealed record AlertmanagerAlertDto(
    string Fingerprint,
    string AlertName,
    string Severity,
    string State,
    string Summary,
    string Description,
    DateTimeOffset StartsAt,
    IReadOnlyDictionary<string, string> Labels,
    IReadOnlyDictionary<string, string> Annotations
);

public sealed record LatencyMetricsDto(
    double AcquirerP95LatencySeconds,
    double OutboxDispatchP95LatencySeconds,
    double DeclineRatePercent,
    long TotalTransactions,
    long TotalDeclines,
    bool AcquirerSlaCompliant,
    bool DeclineRateCompliant
);

public sealed record QueueHealthDto(
    OutboxQueueStatsDto Outbox,
    WebhookDlqStatsDto WebhookDlq,
    UnresolvedPaymentsStatsDto UnresolvedPayments
);

public sealed record OutboxQueueStatsDto(
    long PendingCount,
    long DispatchedCount,
    long FailedCount,
    double? OldestPendingAgeSeconds
);

public sealed record WebhookDlqStatsDto(
    long ExhaustedCount,
    long ReplayedCount,
    long TotalCount
);

public sealed record UnresolvedPaymentsStatsDto(
    long TotalUnresolvedCount
);
