using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Contracts.Events;
using QuickBuilder.Editor.Contracts;
using QuickBuilder.Editor.Domain;
using QuickBuilder.Editor.Infrastructure;
using QuickBuilder.Pages.Contracts.Events;
using QuickBuilder.Shared.Kernel.Events;

namespace QuickBuilder.Editor.Features.ReleaseDeletedDocuments;

/// <summary>
/// Ends the editing sessions of a deleted page, component, item or collection (023 FR-019,
/// research CE-13). Their next heartbeat then answers <c>editing.session_gone</c>, and the
/// editor shows the document is gone.
/// </summary>
/// <remarks>
/// Idempotent: deleting what is not there changes nothing. Each event names its site, which
/// the normal site filter of <see cref="EditorDbContext.ForSite{T}"/> applies.
/// </remarks>
internal sealed class ReleaseDeletedDocumentsHandler(EditorDbContext database)
    : IIntegrationEventHandler<PageDeleted>,
      IIntegrationEventHandler<ComponentDeleted>,
      IIntegrationEventHandler<ItemDeleted>,
      IIntegrationEventHandler<CollectionDeleted>
{
    public Task HandleAsync(PageDeleted integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return EndAsync(integrationEvent.SiteId, EditingDocumentKinds.Page, integrationEvent.PageId, cancellationToken);
    }

    public Task HandleAsync(ComponentDeleted integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return EndAsync(integrationEvent.SiteId, EditingDocumentKinds.Component, integrationEvent.ComponentId, cancellationToken);
    }

    public Task HandleAsync(ItemDeleted integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return EndAsync(integrationEvent.SiteId, EditingDocumentKinds.Item, integrationEvent.ItemId, cancellationToken);
    }

    public Task HandleAsync(CollectionDeleted integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return database.ForSite<EditingSession>(integrationEvent.SiteId)
            .Where(s => s.DocumentKind == EditingDocumentKinds.Item && s.CollectionId == integrationEvent.CollectionId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private Task<int> EndAsync(Guid siteId, string kind, Guid documentId, CancellationToken cancellationToken) =>
        database.ForSite<EditingSession>(siteId)
            .Where(s => s.DocumentKind == kind && s.DocumentId == documentId)
            .ExecuteDeleteAsync(cancellationToken);
}
