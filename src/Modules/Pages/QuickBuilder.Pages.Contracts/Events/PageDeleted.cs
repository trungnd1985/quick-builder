using QuickBuilder.Shared.Kernel.Events;

namespace QuickBuilder.Pages.Contracts.Events;

/// <summary>
/// A page was permanently deleted. Published in the same transaction as the change (001 FR-015).
/// </summary>
/// <remarks>
/// `Path` is the address it had, now free for reuse. Carries ids and paths only — never titles, names or content (010 FR-037).
/// <para>
/// <c>Pages.PageDeleted</c> is a published contract name. Renaming it is a breaking change: add a V2
/// alongside and retire this once nothing subscribes.
/// </para>
/// </remarks>
public sealed record PageDeleted(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    Guid SiteId,
    Guid PageId,
    string Path,
    Guid? ActorUserId) : IIntegrationEvent
{
    public static string EventType => "Pages.PageDeleted";
}
