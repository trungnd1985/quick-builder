using QuickBuilder.Shared.Kernel.Events;

namespace QuickBuilder.Pages.Features.RunDueSchedule;

/// <summary>
/// A schedule's time has come (012 research PR-14). Module-internal: written to the Pages
/// outbox with <c>available_at_utc</c> set to the due time, and handled only by Pages.
/// </summary>
/// <remarks>
/// Carries the schedule's <see cref="Revision"/> when it was written. Changing the time
/// increments the revision and writes a new message, so this one then does nothing.
/// </remarks>
internal sealed record PublishScheduleDue(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    Guid SiteId,
    Guid ScheduleId,
    int Revision) : IIntegrationEvent
{
    public static string EventType => "Pages.PublishScheduleDue";
}
