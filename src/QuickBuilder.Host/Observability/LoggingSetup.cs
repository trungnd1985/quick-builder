using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace QuickBuilder.Host.Observability;

/// <summary>
/// Structured logging to stdout (FR-031, FR-033).
/// </summary>
/// <remarks>
/// Console-only by default: a container's logs belong on stdout, where the orchestrator
/// collects them. Writing files inside a container is how operators lose logs.
/// <para>
/// Levels are read from configuration, globally and per component (FR-033), so raising
/// detail for one noisy namespace needs a restart but never a rebuild.
/// </para>
/// </remarks>
internal static class LoggingSetup
{
    internal static void Configure(
        LoggerConfiguration configuration,
        IConfiguration appConfiguration,
        IServiceProvider services)
    {
        configuration
            .ReadFrom.Configuration(appConfiguration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.With<TraceEnricher>()
            // CompactJsonFormatter: one JSON object per line, which is what log collectors
            // expect and what makes SC-008's "trace one request by its identifier" a grep.
            .WriteTo.Console(new CompactJsonFormatter());
    }
}

/// <summary>
/// Stamps every log entry with the current trace and span id (FR-031a).
/// </summary>
/// <remarks>
/// This is the whole of FR-031a: the correlation identifier in the logs <em>is</em> the
/// trace identifier that OpenTelemetry already assigns from the W3C traceparent header.
/// No second correlation scheme is invented, so logs, traces and the traceId returned in
/// an error response all name the same thing.
/// </remarks>
internal sealed class TraceEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(propertyFactory);

        var activity = Activity.Current;
        if (activity is null)
        {
            return;
        }

        logEvent.AddPropertyIfAbsent(
            propertyFactory.CreateProperty("TraceId", activity.TraceId.ToString()));
        logEvent.AddPropertyIfAbsent(
            propertyFactory.CreateProperty("SpanId", activity.SpanId.ToString()));
    }
}
