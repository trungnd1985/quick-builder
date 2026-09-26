using QuickBuilder.Shared.Kernel.Events;

namespace QuickBuilder.Pages.Contracts.Events;

/// <summary>
/// One operation — a slug change, a move or a new home page — changed page addresses. Published in the same transaction as the change (001 FR-015).
/// </summary>
/// <remarks>
/// Every changed address of the operation is in one event, with its old path, so redirects (#17) can follow them. A reorder within a parent changes no address and publishes nothing (research P-14). Carries ids and paths only — never titles, names or content (010 FR-037).
/// <para>
/// <c>Pages.PageAddressesChanged</c> is a published contract name. Renaming it is a breaking change: add a V2
/// alongside and retire this once nothing subscribes.
/// </para>
/// </remarks>
public sealed record PageAddressesChanged(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    Guid SiteId,
    IReadOnlyList<PageAddressChange> Changes,
    Guid? ActorUserId) : IIntegrationEvent
{
    public static string EventType => "Pages.PageAddressesChanged";
}

/// <summary>One page whose address changed from <see cref="OldPath"/> to <see cref="NewPath"/>.</summary>
public sealed record PageAddressChange(Guid PageId, string OldPath, string NewPath);
