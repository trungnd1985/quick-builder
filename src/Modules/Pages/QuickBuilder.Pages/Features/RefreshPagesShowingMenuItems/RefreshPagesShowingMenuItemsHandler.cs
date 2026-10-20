using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Cms.Contracts.Events;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Kernel.Events;

namespace QuickBuilder.Pages.Features.RefreshPagesShowingMenuItems;

/// <summary>
/// Refreshes the live pages that show a menu pointing at an item, directly or through a
/// component, when the item is published, unpublished or deleted (024 research NV-07, the item
/// rows): its link appears, changes address or label, or disappears.
/// </summary>
/// <remarks>
/// Live menu versions depend on <c>(item, id)</c>, so <see cref="GenerationBump.BumpForTargetsAsync"/>
/// walks item → menus → pages and components → pages. Deduplicated through
/// <see cref="RefreshOnce"/>, under handler names of its own, apart from the list refresh.
/// </remarks>
internal sealed class RefreshPagesShowingMenuItemsHandler(IServiceScopeFactory scopes)
    : IIntegrationEventHandler<ItemPublished>, IIntegrationEventHandler<ItemUnpublished>, IIntegrationEventHandler<ItemDeleted>
{
    private const string PublishedHandlerName = nameof(RefreshPagesShowingMenuItemsHandler) + ".ItemPublished";

    private const string UnpublishedHandlerName = nameof(RefreshPagesShowingMenuItemsHandler) + ".ItemUnpublished";

    private const string DeletedHandlerName = nameof(RefreshPagesShowingMenuItemsHandler) + ".ItemDeleted";

    public Task HandleAsync(ItemPublished integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return RefreshAsync(
            integrationEvent.Id, integrationEvent.OccurredAtUtc, integrationEvent.SiteId, integrationEvent.ItemId, PublishedHandlerName, cancellationToken);
    }

    public Task HandleAsync(ItemUnpublished integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return RefreshAsync(
            integrationEvent.Id, integrationEvent.OccurredAtUtc, integrationEvent.SiteId, integrationEvent.ItemId, UnpublishedHandlerName, cancellationToken);
    }

    public Task HandleAsync(ItemDeleted integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        // A draft-only item was never linked from a live menu.
        return integrationEvent.WasPublished
            ? RefreshAsync(
                integrationEvent.Id, integrationEvent.OccurredAtUtc, integrationEvent.SiteId, integrationEvent.ItemId, DeletedHandlerName, cancellationToken)
            : Task.CompletedTask;
    }

    private Task RefreshAsync(
        Guid eventId, DateTimeOffset occurredAtUtc, Guid siteId, Guid itemId, string handlerName, CancellationToken cancellationToken) =>
        RefreshOnce.RunAsync(
            scopes,
            eventId,
            occurredAtUtc,
            handlerName,
            (bump, ct) => bump.BumpForTargetsAsync(siteId, [(DependencyKinds.Item, itemId)], exceptPageId: null, ct),
            cancellationToken);
}
