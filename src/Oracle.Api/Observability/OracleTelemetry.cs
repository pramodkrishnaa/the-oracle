using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Oracle.Api.Observability;

/// <summary>
/// Custom ActivitySource/Meter for the divination pipeline, registered with OpenTelemetry in
/// Program.cs so every ask produces a span and feeds the latency histogram.
/// </summary>
public static class OracleTelemetry
{
    public const string ServiceName = "Oracle.Api";

    public static readonly ActivitySource ActivitySource = new(ServiceName);

    private static readonly Meter Meter = new(ServiceName);

    public static readonly Counter<long> PropheciesRevealed =
        Meter.CreateCounter<long>("oracle.prophecies.revealed", description: "Number of prophecies revealed, by kind.");

    public static readonly Histogram<double> CosmicLatency =
        Meter.CreateHistogram<double>("oracle.cosmic_latency_ms", unit: "ms", description: "Artificial cosmic consultation latency.");
}
