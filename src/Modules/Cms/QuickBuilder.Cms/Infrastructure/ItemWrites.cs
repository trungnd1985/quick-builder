using Microsoft.EntityFrameworkCore;
using Npgsql;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Cms.Infrastructure;

/// <summary>What an item write is about, and what it was based on (015 FR-005).</summary>
/// <param name="ItemId">The item written, or null when creating one.</param>
/// <param name="ExpectedRevision">The item revision the request was based on, when it states one.</param>
/// <param name="ExpectedSchemaRevision">The schema revision the form was built from, for draft saves (research IT-07).</param>
/// <param name="Actor">Who acts: the signed-in user, or for a scheduled run, who set the schedule.</param>
internal sealed record ItemWriteRequest(
    Guid SiteId,
    Guid CollectionId,
    Guid? ItemId,
    int? ExpectedRevision,
    int? ExpectedSchemaRevision,
    Guid Actor);

/// <summary>What an item operation works with inside <see cref="ItemWrites"/>, and how it says what it changed.</summary>
internal sealed class ItemWriteContext(
    CmsDbContext database,
    ItemValidation validation,
    GateEntry gate,
    ItemWriteRequest request,
    Item? item,
    DateTimeOffset now)
{
    private readonly List<Item> _written = [];
    private readonly List<Item> _deleted = [];

    public CmsDbContext Database => database;

    public ItemValidation Validation => validation;

    public Guid SiteId => request.SiteId;

    public Guid CollectionId => request.CollectionId;

    public CollectionSchemaDocument Schema => gate.Schema;

    public int SchemaRevision => gate.SchemaRevision;

    /// <summary>The item the request names; null when creating.</summary>
    public Item? Item => item;

    public Guid Actor => request.Actor;

    public DateTimeOffset Now => now;

    /// <summary>Whether this operation asked Pages for a redirect (017 FR-029, FR-032).</summary>
    public bool RedirectRequested { get; set; }

    /// <summary>Whether any write in this operation added, removed or migrated versions.</summary>
    public bool VersionsChanged { get; private set; }

    internal IReadOnlyList<Item> Written => _written;

    internal IReadOnlyList<Item> Deleted => _deleted;

    /// <summary>The report of some content of this collection's items, measured today.</summary>
    public Task<ItemReport> ValidateAsync(Guid? itemId, string slug, ItemValues values, CancellationToken cancellationToken) =>
        validation.ValidateAsync(SiteId, itemId, Schema, slug, values, cancellationToken);

    /// <summary>A new item to store.</summary>
    public void Add(Item newItem)
    {
        ArgumentNullException.ThrowIfNull(newItem);
        database.Items.Add(newItem);
        _written.Add(newItem);
    }

    /// <summary>The item was changed; <paramref name="versionsChanged"/> when versions were added or removed.</summary>
    public void Changed(Item changed, bool versionsChanged = false)
    {
        ArgumentNullException.ThrowIfNull(changed);
        if (!_written.Contains(changed))
        {
            _written.Add(changed);
        }

        VersionsChanged |= versionsChanged;
    }

    public void Delete(Item deleted)
    {
        ArgumentNullException.ThrowIfNull(deleted);
        deleted.MarkDeleted(now);
        database.Items.Remove(deleted);
        _deleted.Add(deleted);
    }
}

/// <summary>
/// The one path for every item write (015 research IT-06; 014 data-model §3.3): create, save,
/// duplicate, delete, publish, unpublish, schedule, cancel, rollback, copy to draft and
/// scheduled runs.
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>Begin a transaction, unless the caller already has one.</item>
/// <item>Enter <see cref="CollectionWriteGate"/>: a running schema change refuses the write with
/// <c>cms.items_being_updated</c> (FR-006), and the schema returned stays in force until commit.</item>
/// <item>Check the form's schema revision (<c>cms.schema_changed</c>) and the item's revision
/// (<c>cms.item_conflict</c>, FR-005).</item>
/// <item>Run the operation, which changes aggregates and says what it changed.</item>
/// <item>Save, refresh what is derived through <see cref="ItemIndexer"/>, add history and outbox
/// events, save again, and commit. A failure anywhere rolls all of it back.</item>
/// </list>
/// <c>ItemWritesOnlyTests</c> keeps every item write on this path.
/// </remarks>
internal sealed class ItemWrites(
    CmsDbContext database,
    CollectionWriteGate gate,
    ItemValidation validation,
    ItemIndexer indexer,
    SlugAvailability slugs,
    TimeProvider clock)
{
    /// <summary>
    /// Runs <paramref name="operation"/> as one item write. It returns the item to answer with
    /// (null when there is none, as after a delete), read again after the commit by the caller.
    /// </summary>
    public async Task<Result<Item?>> ExecuteAsync(
        ItemWriteRequest request,
        Func<ItemWriteContext, Task<Result<Item?>>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(operation);

        var owned = database.Database.CurrentTransaction is null;
        var transaction = owned ? await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false) : null;
        ItemWriteContext? context = null;

        try
        {
            var entered = await gate.EnterAsync(request.SiteId, request.CollectionId, null, cancellationToken).ConfigureAwait(false);
            if (entered.IsFailure)
            {
                return await FailAsync(entered.Error).ConfigureAwait(false);
            }

            if (request.ExpectedSchemaRevision is { } expectedSchema && expectedSchema != entered.Value.SchemaRevision)
            {
                return await FailAsync(CmsErrors.SchemaChanged(entered.Value.SchemaRevision)).ConfigureAwait(false);
            }

            Item? item = null;
            if (request.ItemId is { } itemId)
            {
                item = await database.ForSite<Item>(request.SiteId)
                    .SingleOrDefaultAsync(i => i.Id == itemId && i.CollectionId == request.CollectionId, cancellationToken)
                    .ConfigureAwait(false);
                if (item is null)
                {
                    return await FailAsync(CmsErrors.ItemNotFound).ConfigureAwait(false);
                }

                if (request.ExpectedRevision is { } expected && expected != item.Revision)
                {
                    return await FailAsync(CmsErrors.ItemConflict(item.Revision)).ConfigureAwait(false);
                }
            }

            context = new ItemWriteContext(database, validation, entered.Value, request, item, clock.GetUtcNow());
            var result = await operation(context).ConfigureAwait(false);
            if (result.IsFailure)
            {
                return await FailAsync(result.Error).ConfigureAwait(false);
            }

            foreach (var written in context.Written.Where(i => !context.Deleted.Contains(i)))
            {
                database.ItemHistory.AddRange(written.NewHistory);
                written.ClearNewHistory();
            }

            // 016 TB-10: read before the outbox takes the items' events.
            var publishedChanges = RenderStampBump.PublishedChanges(context.Written, context.Deleted);

            foreach (var touched in context.Written.Concat(context.Deleted).Distinct())
            {
                CmsOutbox.AddFrom(database, touched, request.Actor);
            }

            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var toIndex = context.Written.Where(i => !context.Deleted.Contains(i)).ToList();
            await indexer.RefreshManyAsync(toIndex, entered.Value.Schema, context.VersionsChanged, cancellationToken).ConfigureAwait(false);
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // 016 TB-10: item pages that show a changed item through a reference refresh with it.
            await RenderStampBump.BumpReferrersAsync(database, request.SiteId, publishedChanges, cancellationToken).ConfigureAwait(false);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            await RollBackAsync().ConfigureAwait(false);
            var current = await CurrentRevisionAsync(request, cancellationToken).ConfigureAwait(false);
            return Result.Failure<Item?>(current is null ? CmsErrors.ItemNotFound : CmsErrors.ItemConflict(current.Value));
        }
        catch (DbUpdateException ex) when (Constraint(ex, PostgresErrorCodes.UniqueViolation) == "pk_item_slugs")
        {
            var slug = context is { Written.Count: > 0 } ? context.Written[^1].Slug : null;
            await RollBackAsync().ConfigureAwait(false);
            var suggestion = slug is null
                ? string.Empty
                : await slugs.NextFreeAsync(request.SiteId, request.CollectionId, slug, request.ItemId, cancellationToken).ConfigureAwait(false);
            return Result.Failure<Item?>(CmsErrors.ItemSlugTaken(suggestion));
        }
        catch (DbUpdateException ex) when (Constraint(ex, PostgresErrorCodes.ForeignKeyViolation) == "fk_item_references_items_target_item_id")
        {
            // Research IT-10: an item started referring to this one after the delete read its
            // referrers. The delete slice reads them again to answer.
            return await FailAsync(CmsErrors.ItemReferenced([], 0)).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (Constraint(ex, PostgresErrorCodes.UniqueViolation) is "ix_item_schedules_item_id_action" or "pk_item_versions")
        {
            // Another member set the same schedule, or published the same item, at the same moment:
            // the version number they took is the conflict (US2 scenario 7).
            await RollBackAsync().ConfigureAwait(false);
            var current = await CurrentRevisionAsync(request, cancellationToken).ConfigureAwait(false);
            return Result.Failure<Item?>(CmsErrors.ItemConflict(current ?? 0));
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }
        }

        async Task<Result<Item?>> FailAsync(Error error)
        {
            await RollBackAsync().ConfigureAwait(false);
            return Result.Failure<Item?>(error);
        }

        async Task RollBackAsync()
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }

            database.ChangeTracker.Clear();
        }
    }

    private async Task<int?> CurrentRevisionAsync(ItemWriteRequest request, CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        if (request.ItemId is not { } itemId)
        {
            return null;
        }

        return await database.ForSite<Item>(request.SiteId)
            .AsNoTracking()
            .Where(i => i.Id == itemId)
            .Select(i => (int?)i.Revision)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static string? Constraint(DbUpdateException exception, string sqlState) =>
        exception.InnerException is PostgresException postgres && postgres.SqlState == sqlState ? postgres.ConstraintName : null;
}
