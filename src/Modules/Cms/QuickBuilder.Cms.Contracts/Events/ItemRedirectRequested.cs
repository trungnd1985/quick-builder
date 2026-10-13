using QuickBuilder.Shared.Kernel.Events;

namespace QuickBuilder.Cms.Contracts.Events;

/// <summary>
/// A member asked that an item's old address keep working: its published slug changed
/// (<see cref="Reason"/> <c>moved</c>), or it stopped being live (<c>removed</c>), and they
/// accepted the redirect offer (017 FR-028 to FR-032, research SE-11). Written to the Cms
/// outbox in the transaction of that change. Pages creates the redirect.
/// </summary>
/// <remarks>
/// <see cref="OldSlug"/> is the one slug any event carries: an address, sent only when a
/// member asked for the redirect, so Pages can build the old address under the template's
/// pattern. It is content: handlers must never log it (integration-events.md, 017 section).
/// <para>
/// <c>Cms.ItemRedirectRequested</c> is a published contract name. Renaming it is a breaking
/// change: add a V2 alongside and retire this once nothing subscribes.
/// </para>
/// </remarks>
/// <param name="Reason"><c>moved</c> or <c>removed</c>.</param>
/// <param name="TargetPageId">For <c>removed</c>: the page the address should send visitors to.</param>
/// <param name="StatusCode">The redirect's status: <c>301</c>.</param>
public sealed record ItemRedirectRequested(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    Guid SiteId,
    Guid CollectionId,
    Guid ItemId,
    string OldSlug,
    string Reason,
    Guid? TargetPageId,
    int StatusCode,
    Guid? ActorUserId) : IIntegrationEvent
{
    public static string EventType => "Cms.ItemRedirectRequested";
}

/// <summary>Why an item's redirect was requested.</summary>
public static class ItemRedirectReasons
{
    /// <summary>The item's published slug changed; the old address follows the item.</summary>
    public const string Moved = "moved";

    /// <summary>The item stopped being live; the old address goes to a page the member chose.</summary>
    public const string Removed = "removed";
}
