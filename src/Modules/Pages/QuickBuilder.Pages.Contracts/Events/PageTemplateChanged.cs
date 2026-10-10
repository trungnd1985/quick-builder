using QuickBuilder.Shared.Kernel.Events;

namespace QuickBuilder.Pages.Contracts.Events;

/// <summary>
/// A page became the template of a collection, changed collection, or stopped being a template
/// (016 FR-036, research TB-16). Published in the same transaction as the change.
/// </summary>
/// <remarks>
/// The address change that comes with it is announced by <see cref="PageAddressesChanged"/>.
/// Sitemaps and redirects (#17) use both. Carries ids only.
/// <para>
/// <c>Pages.PageTemplateChanged</c> is a published contract name. Renaming it is a breaking
/// change: add a V2 alongside and retire this once nothing subscribes.
/// </para>
/// </remarks>
/// <param name="CollectionId">The collection the page is now the template of; null when it no longer is one.</param>
/// <param name="PreviousCollectionId">The collection it was the template of; null when it was not one.</param>
public sealed record PageTemplateChanged(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    Guid SiteId,
    Guid PageId,
    Guid? CollectionId,
    Guid? PreviousCollectionId,
    Guid? ActorUserId) : IIntegrationEvent
{
    public static string EventType => "Pages.PageTemplateChanged";
}
