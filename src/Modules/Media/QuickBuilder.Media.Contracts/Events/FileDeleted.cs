using QuickBuilder.Shared.Kernel.Events;

namespace QuickBuilder.Media.Contracts.Events;

/// <summary>
/// A media file was permanently deleted from a site's library (012 data-model §3.2). Published
/// in the same transaction as the delete (001 FR-015).
/// </summary>
/// <remarks>
/// The Pages module refreshes the live pages that showed the file, so they stop showing it at
/// their next request. Carries ids only — never the file's name or content.
/// <para>
/// <c>Media.FileDeleted</c> is a published contract name. Renaming it is a breaking change:
/// add a V2 alongside and retire this once nothing subscribes.
/// </para>
/// </remarks>
public sealed record FileDeleted(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    Guid SiteId,
    Guid FileId,
    Guid? ActorUserId) : IIntegrationEvent
{
    public static string EventType => "Media.FileDeleted";
}
