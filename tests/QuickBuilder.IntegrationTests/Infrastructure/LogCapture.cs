using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace QuickBuilder.IntegrationTests.Infrastructure;

/// <summary>
/// Collects every log event the application writes, for assertions about what does and does
/// not reach the logs (002 FR-004a, FR-030; SC-009).
/// </summary>
/// <remarks>
/// Picked up through dependency injection: the host's Serilog configuration reads sinks
/// registered as services, so the application's real logging pipeline — levels, enrichers
/// and all — feeds this. Nothing about logging is replaced.
/// </remarks>
public sealed class LogCapture : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> _events = new();

    public IReadOnlyList<LogEvent> Events => [.. _events];

    public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);

    /// <summary>
    /// Everything a log reader could see for one event: the rendered message, the template,
    /// every property value and the exception text.
    /// </summary>
    public static string Render(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        using var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        writer.Write(logEvent.RenderMessage(System.Globalization.CultureInfo.InvariantCulture));
        writer.Write(' ');
        writer.Write(logEvent.MessageTemplate.Text);

        foreach (var property in logEvent.Properties)
        {
            writer.Write(' ');
            writer.Write(property.Key);
            writer.Write('=');
            property.Value.Render(writer, formatProvider: System.Globalization.CultureInfo.InvariantCulture);
        }

        if (logEvent.Exception is not null)
        {
            writer.Write(' ');
            writer.Write(logEvent.Exception.ToString());
        }

        return writer.ToString();
    }
}
