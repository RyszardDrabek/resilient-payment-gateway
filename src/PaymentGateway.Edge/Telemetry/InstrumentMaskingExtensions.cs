using OpenTelemetry.Logs;
using OpenTelemetry.Trace;

namespace PaymentGateway.Edge.Telemetry;

/// <summary>
/// Registration extensions for the instrument-masking telemetry and log processors (F-EDGE-03).
/// </summary>
public static class InstrumentMaskingExtensions
{
    /// <summary>
    /// Registers <see cref="InstrumentMaskingProcessor"/> into the tracing pipeline.
    /// Call this <em>before</em> <c>.AddOtlpExporter()</c> so masking fires last and
    /// instrument values never reach an exporter in raw form (AC-3).
    /// </summary>
    public static TracerProviderBuilder AddInstrumentMasking(this TracerProviderBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddProcessor(new InstrumentMaskingProcessor());
    }

    /// <summary>
    /// Registers <see cref="LogInstrumentMaskingProcessor"/> into the OpenTelemetry logging pipeline.
    /// Call this <em>before</em> <c>.AddOtlpExporter()</c> so masking fires before logs leave the process.
    /// </summary>
    public static LoggerProviderBuilder AddInstrumentMasking(this LoggerProviderBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddProcessor(new LogInstrumentMaskingProcessor());
    }
}
