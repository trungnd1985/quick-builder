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
    : IIntegrationEventHandler<FileDeleted>, IIntegrationEventHandler<FileChanged>, IIntegrationEventHandler<FileRestored>
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

    /// <summary>021 TR-08: items that missed the file stop needing attention when it is restored.</summary>
    public Task HandleAsync(FileRestored integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return ReindexAsync(integrationEvent.SiteId, integrationEvent.FileId, cancellationToken);
    }

    private async Task ReindexAsync(Guid siteId, Guid fileId, CancellationToken cancellationToken)
    {
        List<(Guid ItemId, Guid CollectionId, bool Live)> users;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            users = (await database.ForSite<ItemMediaUse>(siteId)
                    .AsNoTracking()
                    .Where(m => m.MediaId == fileId)
                    .Join(database.ForSite<Item>(siteId), m => m.ItemId, i => i.Id, (m, i) => new { i.Id, i.CollectionId, m.InLive })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false))
                .Select(i => (i.Id, i.CollectionId, i.InLive))
                .ToList();
        }

        foreach (var (itemId, collectionId, live) in users)
        {
            await using var scope = scopes.CreateAsyncScope();
            var writes = scope.ServiceProvider.GetRequiredService<ItemWrites>();
            var result = await writes.ExecuteAsync(
                    new ItemWriteRequest(siteId, collectionId, itemId, null, null, Guid.Empty),
                    context =>
                    {
                        // 016 TB-10: an item page showing the file renders it anew.
                        if (live)
                        {
                            context.Item!.ShownMediaChanged();
                        }

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
