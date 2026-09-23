using System.Diagnostics;

namespace QuickBuilder.Shared.Infrastructure;

/// <summary>
/// The activity source background work starts activities on (D-10).
/// </summary>
/// <remarks>
/// Outbox dispatch happens outside any request, so nothing would otherwise give it a
/// trace id — and a log line with no correlation identifier is a log line you cannot
/// follow. Starting an activity per dispatch means background work is traceable exactly
/// like request work, and the originating request's trace id travels on the outbox row so
/// the two can be tied together.
/// </remarks>
public static class Telemetry
{
    public const string ActivitySourceName = "QuickBuilder";

    public static ActivitySource Source { get; } = new(ActivitySourceName);
}
