using QuickBuilder.Identity.Domain;

namespace QuickBuilder.Identity.Infrastructure.Security;

/// <summary>
/// Adds security events to the caller's unit of work (spec FR-029).
/// </summary>
/// <remarks>
/// Adds, never saves: the event commits in the same transaction as the change it describes,
/// so an event cannot exist for a change that rolled back, or the reverse. The client
/// address comes from the current request, and is null for the operator command.
/// </remarks>
internal sealed class SecurityEventRecorder(
    IdentityDbContext database,
    IHttpContextAccessor httpContextAccessor,
    TimeProvider clock)
{
    public SecurityEvent Record(
        string eventType,
        string outcome,
        Guid? actorUserId,
        Guid? subjectUserId,
        SecurityEventDetails? details = null)
    {
        var securityEvent = SecurityEvent.Create(
            eventType,
            outcome,
            clock.GetUtcNow(),
            actorUserId,
            subjectUserId,
            httpContextAccessor.HttpContext?.Connection.RemoteIpAddress,
            details);

        database.SecurityEvents.Add(securityEvent);
        return securityEvent;
    }
}
