using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Cms.Contracts.Events;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Kernel.Events;

namespace QuickBuilder.Pages.Features.RefreshPagesListingItems;

/// <summary>
/// Refreshes the live pages that list a collection, directly or through a component, when one
/// of its items' published data changes (016 research TB-10, row 1).
/// </summary>
/// <remarks>
/// Lists of a collection referencing it refresh too: a card may show "Author → Name". Item
/// pages are not bumped: each is keyed by its item's render stamp, which Cms moves in the
/// transaction of the change. Deduplicated through <see cref="RefreshOnce"/>.
/// </remarks>
internal sealed class RefreshPagesListingItemsHandler(IServiceScopeFactory scopes)
    : IIntegrationEventHandler<ItemPublished>, IIntegrationEventHandler<ItemUnpublished>, IIntegrationEventHandler<ItemDeleted>
{
    private const string PublishedHandlerName = nameof(RefreshPagesListingItemsHandler) + ".ItemPublished";

    private const string UnpublishedHandlerName = nameof(RefreshPagesListingItemsHandler) + ".ItemUnpublished";

    private const string DeletedHandlerName = nameof(RefreshPagesListingItemsHandler) + ".ItemDeleted";

    public Task HandleAsync(ItemPublished integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return RefreshAsync(
            integrationEvent.Id, integrationEvent.OccurredAtUtc, integrationEvent.SiteId, integrationEvent.CollectionId, PublishedHandlerName, cancellationToken);
    }

    public Task HandleAsync(ItemUnpublished integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return RefreshAsync(
            integrationEvent.Id, integrationEvent.OccurredAtUtc, integrationEvent.SiteId, integrationEvent.CollectionId, UnpublishedHandlerName, cancellationToken);
    }

    public Task HandleAsync(ItemDeleted integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        // A draft-only item was never on a list.
        return integrationEvent.WasPublished
            ? RefreshAsync(
                integrationEvent.Id, integrationEvent.OccurredAtUtc, integrationEvent.SiteId, integrationEvent.CollectionId, DeletedHandlerName, cancellationToken)
            : Task.CompletedTask;
    }

    private Task RefreshAsync(
        Guid eventId, DateTimeOffset occurredAtUtc, Guid siteId, Guid collectionId, string handlerName, CancellationToken cancellationToken) =>
        RefreshOnce.RunAsync(
            scopes,
            eventId,
            occurredAtUtc,
            handlerName,
            (bump, ct) => bump.BumpForCollectionsAsync(siteId, [collectionId], includeTemplates: false, exceptPageId: null, ct),
            cancellationToken);
}
