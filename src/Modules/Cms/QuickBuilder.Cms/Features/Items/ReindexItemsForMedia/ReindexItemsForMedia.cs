using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.Media.Contracts.Events;
using QuickBuilder.Shared.Kernel.Events;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Cms.Features.Items.ReindexItemsForMedia;

/// <summary>
/// Re-checks the items that use a media file when it is deleted or replaced (015 research IT-11,
/// spec Edge Cases): a deleted image puts its items in "needs attention" (<c>media_missing</c>),
/// and a replaced file's kind is checked again against what its field accepts.
/// </summary>
/// <remarks>
/// Each item is re-indexed through <see cref="ItemWrites"/>, behind the write gate like any item
/// write; its values and revision do not change. A collection whose schema is being changed
/// refuses the write, and the handler throws so the outbox retries it. Idempotent: re-indexing
/// is a pure recomputation.
/// </remarks>
internal sealed class ReindexItemsForMediaHandler(IServiceScopeFactory scopes)
    : IIntegrationEventHandler<FileDeleted>, IIntegrationEventHandler<FileChanged>
{
    public Task HandleAsync(FileDeleted integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return ReindexAsync(integrationEvent.SiteId, integrationEvent.FileId, cancellationToken);
    }

    public Task HandleAsync(FileChanged integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return ReindexAsync(integrationEvent.SiteId, integrationEvent.FileId, cancellationToken);
    }

    private async Task ReindexAsync(Guid siteId, Guid fileId, CancellationToken cancellationToken)
    {
        List<(Guid ItemId, Guid CollectionId)> users;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            var itemIds = database.ForSite<ItemMediaUse>(siteId).Where(m => m.MediaId == fileId).Select(m => m.ItemId);
            users = (await database.ForSite<Item>(siteId)
                    .AsNoTracking()
                    .Where(i => itemIds.Contains(i.Id))
                    .Select(i => new { i.Id, i.CollectionId })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false))
                .Select(i => (i.Id, i.CollectionId))
                .ToList();
        }

        foreach (var (itemId, collectionId) in users)
        {
            await using var scope = scopes.CreateAsyncScope();
            var writes = scope.ServiceProvider.GetRequiredService<ItemWrites>();
            var result = await writes.ExecuteAsync(
                    new ItemWriteRequest(siteId, collectionId, itemId, null, null, Guid.Empty),
                    context =>
                    {
                        context.Changed(context.Item!);
                        return Task.FromResult(Result.Success<Item?>(context.Item));
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            if (result.IsFailure && result.Error.Code == CmsErrors.ItemsBeingUpdated.Code)
            {
                throw new InvalidOperationException("A schema change holds the collection; the outbox retries this event shortly.");
            }
        }
    }
}
