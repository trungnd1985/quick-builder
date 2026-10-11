using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Cms.Contracts.Events;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Kernel.Events;

namespace QuickBuilder.Pages.Features.RefreshPagesUsingCollection;

/// <summary>
/// Refreshes every live page that lists or shows a collection, every item page of its
/// template included, when its fields change or its stored values are rewritten, and when it
/// is deleted (016 research TB-10, row 2; closes E-6).
/// </summary>
/// <remarks>
/// A change to the name or description alone is not shown on pages and refreshes nothing.
/// Bindings to removed fields render empty at the next request (FR-018). Deduplicated through
/// <see cref="RefreshOnce"/>.
/// </remarks>
internal sealed class RefreshPagesUsingCollectionHandler(IServiceScopeFactory scopes)
    : IIntegrationEventHandler<CollectionChanged>, IIntegrationEventHandler<CollectionDeleted>
{
    private const string ChangedHandlerName = nameof(RefreshPagesUsingCollectionHandler) + ".CollectionChanged";

    private const string DeletedHandlerName = nameof(RefreshPagesUsingCollectionHandler) + ".CollectionDeleted";

    public Task HandleAsync(CollectionChanged integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (!integrationEvent.SchemaChanged && !integrationEvent.ItemsRewritten)
        {
            return Task.CompletedTask;
        }

        return RefreshAsync(
            integrationEvent.Id, integrationEvent.OccurredAtUtc, integrationEvent.SiteId, integrationEvent.CollectionId, ChangedHandlerName, cancellationToken);
    }

    public Task HandleAsync(CollectionDeleted integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return RefreshAsync(
            integrationEvent.Id, integrationEvent.OccurredAtUtc, integrationEvent.SiteId, integrationEvent.CollectionId, DeletedHandlerName, cancellationToken);
    }

    private Task RefreshAsync(
        Guid eventId, DateTimeOffset occurredAtUtc, Guid siteId, Guid collectionId, string handlerName, CancellationToken cancellationToken) =>
        RefreshOnce.RunAsync(
            scopes,
            eventId,
            occurredAtUtc,
            handlerName,
            (bump, ct) => bump.BumpForCollectionsAsync(siteId, [collectionId], includeTemplates: true, exceptPageId: null, ct),
            cancellationToken);
}
