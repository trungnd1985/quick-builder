using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Domain;

namespace QuickBuilder.Cms.Infrastructure;

/// <summary>
/// Rewrites a collection's items for a save, in batches of 500, inside the save's transaction
/// (014 research CS-09, FR-021, clarification Q1; 015 research IT-11, IT-12).
/// </summary>
/// <remarks>
/// <para>
/// Every stored copy of an item follows the change: its draft and published values (014), and
/// from 015 its kept versions and the captured content of a pending scheduled publish, so a
/// removed or cleared value is gone from all of them (014 FR-024). Fill-in values go to drafts,
/// published values and scheduled content, never to versions, which record what was published.
/// </para>
/// <para>
/// Only copies whose values change are written, so a rename or reorder writes none (014 SC-002).
/// Rewritten items move their revision, so an open form cannot overwrite them, and are
/// re-indexed against the new schema; when the change can alter names or attention without
/// changing a value (title field, required, settings, type), every item is re-indexed. Each
/// batch is saved and detached, so memory stays flat; nothing is visible to readers until the
/// caller commits, and a failure anywhere rolls every batch back (014 SC-005).
/// </para>
/// </remarks>
internal sealed class ItemMigrator(
    CmsDbContext database,
    ValueMigration migration,
    MediaKinds mediaKinds,
    ItemIndexer indexer,
    ISchemaMigrationObserver observer,
    TimeProvider clock)
{
    internal const int BatchSize = 500;

    /// <summary>
    /// Applies the plans to every copy of the collection's items and refreshes what is derived;
    /// returns how many items were rewritten.
    /// </summary>
    /// <param name="after">The schema as it will be stored, with permanent ids.</param>
    /// <param name="reindexAll">Whether every item must be re-indexed, even one whose values do not change.</param>
    public async Task<int> MigrateAsync(
        Collection collection,
        IReadOnlyList<FieldPlan> plans,
        CollectionSchemaDocument after,
        bool reindexAll,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentNullException.ThrowIfNull(after);

        var active = plans.Where(p => p.Rule != ChangeRule.Forbidden).ToList();
        if (active.Count == 0 && !reindexAll)
        {
            return 0;
        }

        var touched = new HashSet<Guid>();
        if (active.Count > 0)
        {
            touched.UnionWith(await MigrateVersionsAsync(collection, active, cancellationToken).ConfigureAwait(false));
            touched.UnionWith(await MigrateSchedulesAsync(collection, active, cancellationToken).ConfigureAwait(false));
        }

        var versionsChanged = touched.Count > 0;
        var rewritten = 0;
        var batchNumber = 0;
        while (true)
        {
            // Offset paging is stable here: the collection is locked, so no item is added or
            // removed while it runs, and a rewritten item keeps its place in id order.
            var batch = await database.ForSite<Item>(collection.SiteId)
                .Where(i => i.CollectionId == collection.Id)
                .OrderBy(i => i.Id)
                .Skip(batchNumber * BatchSize)
                .Take(BatchSize).ToListAsync(cancellationToken).ConfigureAwait(false);
            if (batch.Count == 0)
            {
                break;
            }

            var media = active.Any(p => p.InvolvesMedia)
                ? await mediaKinds.LookupAsync(collection.SiteId, MediaValues(batch, active), cancellationToken).ConfigureAwait(false)
                : static _ => MediaKind.Missing;

            var toIndex = new List<Item>();
            foreach (var item in batch)
            {
                var draft = item.DraftValues();
                var published = item.PublishedValues();
                var changed = Apply(active, draft, media, fillIns: true);
                changed |= published is not null && Apply(active, published, media, fillIns: true);

                if (changed)
                {
                    item.Migrate(draft, published, clock);
                    rewritten++;
                }

                if (changed || reindexAll || touched.Contains(item.Id))
                {
                    toIndex.Add(item);
                }
            }

            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await indexer.RefreshManyAsync(toIndex, after, versionsChanged, cancellationToken).ConfigureAwait(false);
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            DetachItemRows();
            await observer.AfterBatchAsync(++batchNumber, cancellationToken).ConfigureAwait(false);
        }

        return rewritten;
    }

    /// <summary>
    /// Converts kept versions that hold a value in a changed field, without fill-ins (research
    /// IT-12); returns the items whose versions changed.
    /// </summary>
    private async Task<HashSet<Guid>> MigrateVersionsAsync(Collection collection, List<FieldPlan> plans, CancellationToken cancellationToken)
    {
        var withoutFillIns = plans.Where(p => p.Before is not null).Select(p => p with { FillIn = null }).ToList();
        var fieldIds = withoutFillIns.Select(p => p.FieldId).ToArray();
        var touched = new HashSet<Guid>();
        if (fieldIds.Length == 0)
        {
            return touched;
        }

        // The keys are read first: migrating a version can take it out of the filter, which
        // would shift any offset paging over it.
        var keys = await VersionKeysAsync(collection, fieldIds, cancellationToken).ConfigureAwait(false);
        foreach (var chunk in keys.Chunk(BatchSize))
        {
            var itemIds = chunk.Select(k => k.ItemId).Distinct().ToList();
            var wanted = chunk.ToHashSet();
            var batch = (await database.ForSite<ItemVersion>(collection.SiteId)
                    .Where(v => itemIds.Contains(v.ItemId))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false))
                .Where(v => wanted.Contains((v.ItemId, v.Number)))
                .ToList();

            var media = withoutFillIns.Any(p => p.InvolvesMedia)
                ? await mediaKinds.LookupAsync(collection.SiteId, batch.SelectMany(v => fieldIds.Select(id => v.ReadValues().Get(id))), cancellationToken).ConfigureAwait(false)
                : static _ => MediaKind.Missing;

            foreach (var version in batch)
            {
                var values = version.ReadValues();
                if (Apply(withoutFillIns, values, media, fillIns: false))
                {
                    version.Migrate(values);
                    touched.Add(version.ItemId);
                }
            }

            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            DetachItemRows();
        }

        return touched;
    }

    /// <summary>
    /// The versions of the collection's items that hold a value in one of the fields, in key
    /// order. The filter runs in the database (<c>?|</c> over the values object); where Npgsql
    /// cannot translate it, the versions are filtered here instead, which costs reading, not
    /// correctness (research IT-12).
    /// </summary>
    private async Task<List<(Guid ItemId, int Number)>> VersionKeysAsync(Collection collection, string[] fieldIds, CancellationToken cancellationToken)
    {
        var items = database.ForSite<Item>(collection.SiteId).Where(i => i.CollectionId == collection.Id).Select(i => i.Id);
        var versions = database.ForSite<ItemVersion>(collection.SiteId).Where(v => items.Contains(v.ItemId));

        try
        {
            return (await versions
                    .Where(v => EF.Functions.JsonExistAny(v.Values.RootElement.GetProperty("values"), fieldIds))
                    .OrderBy(v => v.ItemId).ThenBy(v => v.Number)
                    .Select(v => new { v.ItemId, v.Number })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false))
                .Select(k => (k.ItemId, k.Number))
                .ToList();
        }
        catch (InvalidOperationException)
        {
            var all = await versions
                .AsNoTracking()
                .OrderBy(v => v.ItemId).ThenBy(v => v.Number)
                .Select(v => new { v.ItemId, v.Number, v.Values })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            return all
                .Where(v => { var values = ItemValues.Read(v.Values); return fieldIds.Any(values.Has); })
                .Select(v => (v.ItemId, v.Number))
                .ToList();
        }
    }

    /// <summary>
    /// Detaches the item rows a batch loaded or added, so memory stays flat. The collection the
    /// save is changing stays tracked: the caller stores it after the migration.
    /// </summary>
    private void DetachItemRows()
    {
        foreach (var entry in database.ChangeTracker.Entries().Where(e => e.Entity is not Collection).ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    /// <summary>Converts the captured content of pending scheduled publishes, with fill-ins (FR-025).</summary>
    private async Task<HashSet<Guid>> MigrateSchedulesAsync(Collection collection, List<FieldPlan> plans, CancellationToken cancellationToken)
    {
        var items = database.ForSite<Item>(collection.SiteId).Where(i => i.CollectionId == collection.Id).Select(i => i.Id);
        var schedules = await database.ForSite<ItemSchedule>(collection.SiteId)
            .Where(s => s.State == ItemScheduleStates.Pending && s.Action == ItemScheduleActions.Publish && s.CapturedValues != null)
            .Where(s => items.Contains(s.ItemId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var touched = new HashSet<Guid>();
        if (schedules.Count == 0)
        {
            return touched;
        }

        var media = plans.Any(p => p.InvolvesMedia)
            ? await mediaKinds.LookupAsync(collection.SiteId, schedules.SelectMany(s => plans.Select(p => s.ReadCaptured()!.Get(p.FieldId))), cancellationToken).ConfigureAwait(false)
            : static _ => MediaKind.Missing;

        foreach (var schedule in schedules)
        {
            var values = schedule.ReadCaptured()!;
            if (Apply(plans, values, media, fillIns: true))
            {
                schedule.Migrate(values);
                touched.Add(schedule.ItemId);
            }
        }

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        DetachItemRows();
        return touched;
    }

    /// <summary>Applies every plan to one copy; whether any value changed.</summary>
    private bool Apply(IReadOnlyList<FieldPlan> plans, ItemValues values, Func<Guid, MediaKind> media, bool fillIns)
    {
        var changed = false;
        foreach (var plan in plans)
        {
            if (migration.Apply(plan, values.Get(plan.FieldId), media) is not { } result)
            {
                continue;
            }

            if (result.Outcome is ValueOutcome.Cleared or ValueOutcome.PartlyLost or ValueOutcome.Converted
                || (fillIns && result.Outcome == ValueOutcome.Filled))
            {
                values.Set(plan.FieldId, result.Value);
                changed = true;
            }
        }

        return changed;
    }

    private static IEnumerable<JsonNode?> MediaValues(List<Item> batch, List<FieldPlan> plans) =>
        batch.SelectMany(i =>
        {
            var draft = i.DraftValues();
            var published = i.PublishedValues();
            return plans.Where(p => p.InvolvesMedia).SelectMany(p => new[] { draft.Get(p.FieldId), published?.Get(p.FieldId) });
        });
}
