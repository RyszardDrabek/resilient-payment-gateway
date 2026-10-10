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

    public static void RecordTransaction(string status, string channel, string operation = "authorize")
    {
        TransactionsCounter.Add(1,
            new KeyValuePair<string, object?>("status", status),
            new KeyValuePair<string, object?>("channel", channel),
            new KeyValuePair<string, object?>("operation", operation));
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
}
