using QuickBuilder.Shared.Kernel.Events;

namespace QuickBuilder.Sites.Contracts.Events;

/// <summary>
/// A site was created. Published in the same transaction as the site itself (FR-015).
/// </summary>
/// <remarks>
/// Carries identifiers and the few fields a handler is likely to need, not the whole
/// aggregate — a handler wanting more reads through <see cref="ISiteDirectory"/>.
/// <para>
/// <c>Sites.SiteCreated</c> is a published contract name. Renaming it is a breaking
/// change: add <c>Sites.SiteCreatedV2</c> alongside and retire this once nothing
/// subscribes.
/// </para>
/// </remarks>
public sealed record SiteCreated(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    Guid SiteId,
    string Name,
    IReadOnlyList<string> HostNames) : IIntegrationEvent
{
    public static string EventType => "Sites.SiteCreated";
}
