using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Shared.Infrastructure.Trash;

namespace QuickBuilder.Cms.Infrastructure;

/// <summary>A collection, with the site it belongs to.</summary>
internal sealed record SiteCollection(Guid SiteId, Guid CollectionId);

/// <summary>
/// Indexes items whose derived data is missing or older than <see cref="ItemIndexer.CurrentVersion"/>
/// (015 research IT-11, IT-22), after start-up.
/// </summary>
/// <remarks>
/// <para>
/// The <c>AddItems</c> migration can only emit SQL, so items stored by 014 got placeholder
/// slugs and no name, search text, attention or derived rows. This gives them all of that, and
/// replaces each placeholder slug with one built from the item's name, made unique. A future
/// change to what the indexer derives raises its version, and this runs again.
/// </para>
/// <para>
/// <b>One collection at a time</b>, each in batches of 500 through <see cref="CollectionWriteGate"/>
/// like any item write: a collection whose schema is being changed is skipped and picked up at
/// the next start. Idempotent: an item edited meanwhile was indexed by its own write, and the
/// revision turns this save into a no-op.
/// </para>
/// </remarks>
internal sealed class ItemIndexBackfillService(
    IServiceScopeFactory scopes,
    IHostApplicationLifetime lifetime,
    ILogger<ItemIndexBackfillService> logger) : BackgroundService
{
    internal const int BatchSize = 500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // After readiness, never in front of it.
        if (!await ApplicationStartedAsync(stoppingToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            await RunOnceAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Cms item index backfill failed; it runs again at the next start.");
        }
    }

    /// <summary>Indexes every item below the current version, collection by collection. Returns how many. Exposed for tests.</summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        List<SiteCollection> collections;
        await using (var scope = scopes.CreateAsyncScope())
        {
            collections = await scope.ServiceProvider.GetRequiredService<CmsDbContext>()
                .CollectionsToIndexAsync(ItemIndexer.CurrentVersion, cancellationToken)
                .ConfigureAwait(false);
        }

        var total = 0;
        foreach (var collection in collections)
        {
            var indexed = await RunForCollectionAsync(collection, cancellationToken).ConfigureAwait(false);
            if (indexed > 0)
            {
                logger.LogInformation(
                    "Cms item index backfill indexed {Count} item(s) of collection {CollectionId} on site {SiteId}.",
                    indexed, collection.CollectionId, collection.SiteId);
            }

            total += indexed;
        }

        return total;
    }

    private async Task<int> RunForCollectionAsync(SiteCollection target, CancellationToken cancellationToken)
    {
        var indexed = 0;
        while (true)
        {
            await using var scope = scopes.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            var gate = scope.ServiceProvider.GetRequiredService<CollectionWriteGate>();
            var indexer = scope.ServiceProvider.GetRequiredService<ItemIndexer>();
            var slugs = scope.ServiceProvider.GetRequiredService<SlugAvailability>();

            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var entered = await gate.EnterAsync(target.SiteId, target.CollectionId, null, cancellationToken).ConfigureAwait(false);
            if (entered.IsFailure)
            {
                // A schema change holds it, or it was deleted: the next start picks it up.
                return indexed;
            }

            var batch = await database.ForSite<Item>(target.SiteId)
                .IncludingTrash("Trashed items are re-indexed too; the indexer keeps their slugs and references out (021 TR-04, TR-18).")
                .Where(i => i.CollectionId == target.CollectionId && i.IndexVersion < ItemIndexer.CurrentVersion)
                .OrderBy(i => i.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (batch.Count == 0)
            {
                return indexed;
            }

            await ReplacePlaceholderSlugsAsync(target, batch, entered.Value.Schema, slugs, cancellationToken).ConfigureAwait(false);

            try
            {
                await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await indexer.RefreshManyAsync(batch, entered.Value.Schema, versionsChanged: true, cancellationToken).ConfigureAwait(false);
                await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                indexed += batch.Count;
            }
            catch (DbUpdateException)
            {
                // An item was written meanwhile, and indexed by its own write. The next batch
                // reads what is still left.
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                return indexed;
            }
        }
    }

    /// <summary>Gives items still holding a placeholder slug one built from their name, unique in the collection.</summary>
    private static async Task ReplacePlaceholderSlugsAsync(
        SiteCollection target,
        List<Item> batch,
        CollectionSchemaDocument schema,
        SlugAvailability slugs,
        CancellationToken cancellationToken)
    {
        var chosen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in batch.Where(i => i.IndexVersion == 0 && i.Slug == Item.PlaceholderSlug(i.Id)))
        {
            var name = schema.TitleField is { } title && item.DraftValues().Get(title.Id) is System.Text.Json.Nodes.JsonValue value
                && value.TryGetValue<string>(out var text) ? text : null;
            var fromName = SlugRules.FromName(name);
            if (fromName.Length == 0)
            {
                continue;
            }

            var free = await slugs.FirstFreeAsync(
                    target.SiteId,
                    target.CollectionId,
                    SlugRules.Candidates(fromName).Where(c => !chosen.Contains(c)),
                    item.Id,
                    cancellationToken)
                .ConfigureAwait(false);
            chosen.Add(free);
            item.ReplacePlaceholderSlug(free);
        }
    }

    private async Task<bool> ApplicationStartedAsync(CancellationToken stoppingToken)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var onStarted = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        using var onStopping = stoppingToken.Register(() => started.TrySetCanceled(stoppingToken));

        try
        {
            await started.Task.ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
