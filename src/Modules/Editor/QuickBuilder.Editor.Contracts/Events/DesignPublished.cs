using QuickBuilder.Shared.Kernel.Events;

namespace QuickBuilder.Editor.Contracts.Events;

/// <summary>
/// A site's design version became live: a publish or a rollback (012 research PR-19). Published
/// in the same transaction as the change (001 FR-015).
/// </summary>
/// <remarks>
/// The Pages module records it in the site's publish log (PR-16). Carries numbers only — never
/// token names or values (FR-044).
/// <para>
/// <c>Editor.DesignPublished</c> is a published contract name. Renaming it is a breaking change:
/// add a V2 alongside and retire this once nothing subscribes.
/// </para>
/// </remarks>
/// <param name="Kind"><c>publish</c> or <c>rollback</c>.</param>
public sealed record DesignPublished(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    Guid SiteId,
    int VersionNumber,
    string Kind,
    int? RestoredFromNumber,
    Guid? ActorUserId) : IIntegrationEvent
{
    public static string EventType => "Editor.DesignPublished";
}
