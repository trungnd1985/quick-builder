using QuickBuilder.Shared.Kernel.Events;

namespace QuickBuilder.Media.Contracts.Events;

/// <summary>
/// A media file was taken out of a site's trash (021 TR-08). It is served again at its own
/// address at once. Published in the same transaction as the restore (001 FR-015).
/// </summary>
/// <remarks>
/// The Pages module refreshes the live pages that show the file, so they show it again at their
/// next request (FR-026); Cms re-indexes the items that use it. Carries ids only.
/// <para>
/// <c>Media.FileRestored</c> is a published contract name. Renaming it is a breaking change.
/// </para>
/// </remarks>
public sealed record FileRestored(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    Guid SiteId,
    Guid FileId,
    Guid? ActorUserId) : IIntegrationEvent
{
    public static string EventType => "Media.FileRestored";
}
