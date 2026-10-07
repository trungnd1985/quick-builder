using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Domain;

namespace QuickBuilder.Cms.Infrastructure;

/// <summary>
/// Rewrites a collection's items for a save, draft and published copies together, in batches
/// of 500, inside the save's transaction (research CS-09, FR-021, clarification Q1).
/// </summary>
/// <remarks>
/// The only writer of item values in this feature. Only items whose values change are
/// written, so a rename or reorder writes none (SC-002). Each batch is saved and its items
/// detached, so memory stays flat; nothing is visible to readers until the caller commits,
/// and a failure anywhere rolls every batch back (SC-005).
/// </remarks>
internal sealed class ItemMigrator(
    CmsDbContext database,
    ValueMigration migration,
    MediaKinds mediaKinds,
    ISchemaMigrationObserver observer,
    TimeProvider clock)
{
    internal const int BatchSize = 500;

    /// <summary>Applies the plans to every item of the collection; returns how many items were rewritten.</summary>
    public async Task<int> MigrateAsync(Collection collection, IReadOnlyList<FieldPlan> plans, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(plans);

        var active = plans.Where(p => p.Rule != ChangeRule.Forbidden).ToList();
        if (active.Count == 0)
        {
            return 0;
        }

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

            foreach (var item in batch)
            {
                var draft = item.DraftValues();
                var published = item.PublishedValues();
                var changed = Apply(active, draft, media);
                changed |= published is not null && Apply(active, published, media);

                if (changed)
                {
                    item.Migrate(draft, published, clock);
                    rewritten++;
                }
            }

            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            foreach (var item in batch)
            {
                database.Entry(item).State = EntityState.Detached;
            }

            await observer.AfterBatchAsync(++batchNumber, cancellationToken).ConfigureAwait(false);
        }

        return rewritten;
    }

    /// <summary>Applies every plan to one copy; whether any value changed.</summary>
    private bool Apply(IReadOnlyList<FieldPlan> plans, ItemValues values, Func<Guid, MediaKind> media)
    {
        var changed = false;
        foreach (var plan in plans)
        {
            if (migration.Apply(plan, values.Get(plan.FieldId), media) is not { } result)
            {
                continue;
            }

            if (result.Outcome is ValueOutcome.Cleared or ValueOutcome.PartlyLost or ValueOutcome.Converted or ValueOutcome.Filled)
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
