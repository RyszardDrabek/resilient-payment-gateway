using System.Diagnostics.Metrics;

namespace PaymentGateway.Application.Metrics;

public static class PaymentMetrics
{
    public const string MeterName = "PaymentGateway";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    private static readonly Counter<long> TransactionsCounter =
        Meter.CreateCounter<long>("payment_transactions_total", "transactions", "Total count of processed payment transactions");

    private static readonly Counter<long> DeclinesCounter =
        Meter.CreateCounter<long>("payment_declines_total", "transactions", "Total count of declined payment operations");

    private static readonly Histogram<double> LatencyHistogram =
        Meter.CreateHistogram<double>("payment_handler_duration_seconds", "s", "Duration of payment handling in seconds");

    private static readonly Counter<long> VolumeCounter =
        Meter.CreateCounter<long>("payment_volume_total", "minor_units", "Total monetary volume of processed payments in minor currency units");

    private static readonly Histogram<double> ChannelLatencyHistogram =
        Meter.CreateHistogram<double>("payment_channel_duration_seconds", "s", "Duration of external settlement channel execution in seconds");

    private static readonly Counter<long> WebhookDispatchCounter =
        Meter.CreateCounter<long>("merchant_webhook_dispatched_total", "dispatches", "Total count of outbound merchant webhook delivery attempts");

    private static readonly Counter<long> WebhookReplayCounter =
        Meter.CreateCounter<long>("merchant_webhook_replay_attempts_total", "replays", "Total count of manual webhook DLQ replay attempts");

    private static readonly Histogram<double> WebhookLatencyHistogram =
        Meter.CreateHistogram<double>("merchant_webhook_dispatch_duration_seconds", "s", "Duration of outbound webhook dispatch in seconds");

    private static readonly Counter<long> OutboxDispatchCounter =
        Meter.CreateCounter<long>("outbox_messages_dispatched_total", "messages", "Total count of dispatched outbox messages");

    private static readonly Counter<long> OutboxDeadLetterCounter =
        Meter.CreateCounter<long>("outbox_messages_dead_lettered_total", "messages", "Total count of dead-lettered outbox messages");

    private static readonly Histogram<double> OutboxLatencyHistogram =
        Meter.CreateHistogram<double>("outbox_dispatch_duration_seconds", "s", "Duration of outbox message publishing in seconds");


    public static void RecordTransaction(string status, string channel, string operation = "authorize")
    {
        TransactionsCounter.Add(1,
            new KeyValuePair<string, object?>("status", status),
            new KeyValuePair<string, object?>("channel", channel),
            new KeyValuePair<string, object?>("operation", operation));
    }

    public static void RecordVolume(long amount, string currency, string channel, string operation, string status)
    {
        VolumeCounter.Add(amount,
            new KeyValuePair<string, object?>("currency", currency),
            new KeyValuePair<string, object?>("channel", channel),
            new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("status", status));
    }

    public static void RecordDecline(string reason, string channel)
    {
        DeclinesCounter.Add(1,
            new KeyValuePair<string, object?>("reason", reason),
            new KeyValuePair<string, object?>("channel", channel));
    }

    public static void RecordDuration(double durationSeconds, string operation, string status)
    {
        LatencyHistogram.Record(durationSeconds,
            new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("status", status));
    }

    public static void RecordChannelDuration(double durationSeconds, string channel, string operation, string status)
    {
        ChannelLatencyHistogram.Record(durationSeconds,
            new KeyValuePair<string, object?>("channel", channel),
            new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("status", status));
    }

    public static void RecordWebhookDispatch(string status, int? statusCode = null)
    {
        WebhookDispatchCounter.Add(1,
            new KeyValuePair<string, object?>("status", status),
            new KeyValuePair<string, object?>("status_code", statusCode?.ToString() ?? "none"));
    }

    public static void RecordWebhookReplay(string outcome, int? statusCode = null)
    {
        WebhookReplayCounter.Add(1,
            new KeyValuePair<string, object?>("outcome", outcome),
            new KeyValuePair<string, object?>("status_code", statusCode?.ToString() ?? "none"));
    }

    public static void RecordWebhookDuration(double durationSeconds, string status)
    {
        WebhookLatencyHistogram.Record(durationSeconds,
            new KeyValuePair<string, object?>("status", status));
    }

    public static void RecordOutboxDispatch(string status)
    {
        OutboxDispatchCounter.Add(1,
            new KeyValuePair<string, object?>("status", status));
    }

    public static void RecordOutboxDeadLettered()
    {
        OutboxDeadLetterCounter.Add(1);
    }

    public static void RecordOutboxDuration(double durationSeconds)
    {
        OutboxLatencyHistogram.Record(durationSeconds);
    }
}

