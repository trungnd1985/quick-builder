using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Shared.Kernel.Text;

namespace QuickBuilder.Cms.Infrastructure;

/// <summary>
/// Keeps everything derived from an item in step with its values (015 research IT-11): its
/// name, search text, attention report and status, and its rows in <c>item_slugs</c>,
/// <c>item_references</c> and <c>item_media</c>.
/// </summary>
/// <remarks>
/// <para>
/// One function for every writer: the item write path, the schema migration, the media event
/// handlers and the start-up backfill. It reads the items' saved state, so callers save the
/// operation first and refresh in the same transaction. Refreshing twice changes nothing.
/// </para>
/// <para>
/// It works on a batch with a fixed number of queries, whatever its size: one for pending
/// scheduled content, one for kept versions (only when they changed), one for referenced
/// items, one call to Media for file kinds, and one delete per derived table. A reference to an
/// item that does not exist is reported, never stored, so the target's foreign key holds.
/// </para>
/// </remarks>
internal sealed class ItemIndexer(CmsDbContext database, ItemValidation validation, MediaKinds mediaKinds)
{
    /// <summary>
    /// Raised whenever what is derived, or how, changes: items below it are refreshed by
    /// <c>ItemIndexBackfillService</c> at start-up.
    /// </summary>
    public const int CurrentVersion = 1;

    public Task RefreshAsync(Item item, CollectionSchemaDocument schema, bool versionsChanged, CancellationToken cancellationToken) =>
        RefreshManyAsync([item], schema, versionsChanged, cancellationToken);

    /// <summary>
    /// Refreshes items of one collection. <paramref name="versionsChanged"/> says whether their
    /// kept versions changed in this write; otherwise the media history flags are carried over.
    /// </summary>
    public async Task RefreshManyAsync(IReadOnlyList<Item> items, CollectionSchemaDocument schema, bool versionsChanged, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(schema);
        if (items.Count == 0)
        {
            return;
        }

        var siteId = items[0].SiteId;
        var ids = items.Select(i => i.Id).ToList();

        var captured = (await database.ForSite<ItemSchedule>(siteId)
                .AsNoTracking()
                .Where(s => ids.Contains(s.ItemId) && s.State == ItemScheduleStates.Pending && s.Action == ItemScheduleActions.Publish)
                .Select(s => new { s.ItemId, s.CapturedValues })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .Where(s => s.CapturedValues is not null)
            .ToDictionary(s => s.ItemId, s => ItemValues.Read(s.CapturedValues!));

        var history = versionsChanged
            ? await HistoryMediaAsync(siteId, items, schema, cancellationToken).ConfigureAwait(false)
            : await KeptHistoryMediaAsync(siteId, ids, cancellationToken).ConfigureAwait(false);

        var copies = items.ToDictionary(i => i.Id, i => new Copies(i.DraftValues(), i.PublishedValues(), captured.GetValueOrDefault(i.Id)));

        var referenced = copies.Values
            .SelectMany(c => c.All)
            .SelectMany(v => ReferenceIds(schema, v))
            .ToHashSet();
        var collectionOf = await new ItemReferenceLookup(database)
            .CollectionsOfAsync(siteId, referenced, cancellationToken)
            .ConfigureAwait(false);

        var kindOf = await mediaKinds
            .LookupAsync(siteId, copies.Values.SelectMany(c => MediaValues(schema, c.Draft)), cancellationToken)
            .ConfigureAwait(false);

        var lookups = new BatchLookups(kindOf, collectionOf);

        await database.ForSite<ItemSlug>(siteId).Where(s => ids.Contains(s.ItemId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await database.ForSite<ItemReference>(siteId).Where(r => ids.Contains(r.SourceItemId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await database.ForSite<ItemMediaUse>(siteId).Where(m => ids.Contains(m.ItemId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

        foreach (var item in items)
        {
            var copy = copies[item.Id];
            var report = await validation
                .ValidateAsync(siteId, item.Id, schema, item.Slug, copy.Draft, lookups, lookups, cancellationToken)
                .ConfigureAwait(false);

            var name = schema.TitleField is { } title && copy.Draft.Get(title.Id) is JsonValue value && value.TryGetValue<string>(out var text)
                ? text
                : null;
            var searchText = name is null ? SearchText.Normalise(item.Slug) : SearchText.Combine(name, item.Slug);
            item.ApplyIndex(name, searchText, report, CurrentVersion);

            database.ItemSlugs.Add(new ItemSlug(siteId, item.CollectionId, item.Slug, item.Id));
            if (item.PublishedSlug is { } publishedSlug && !string.Equals(publishedSlug, item.Slug, StringComparison.OrdinalIgnoreCase))
            {
                database.ItemSlugs.Add(new ItemSlug(siteId, item.CollectionId, publishedSlug, item.Id));
            }

            database.ItemReferences.AddRange(References(item, schema, copy, collectionOf));
            database.ItemMedia.AddRange(Media(item, schema, copy, history.GetValueOrDefault(item.Id) ?? []));
        }
    }

    private static IEnumerable<ItemReference> References(Item item, CollectionSchemaDocument schema, Copies copy, IReadOnlyDictionary<Guid, Guid> collectionOf)
    {
        foreach (var field in schema.Fields.Where(f => f.Type is FieldType.Reference or FieldType.MultiReference))
        {
            var draft = IdsOf(field, copy.Draft);
            var published = copy.Published is null ? [] : IdsOf(field, copy.Published);
            var scheduled = copy.Captured is null ? [] : IdsOf(field, copy.Captured);

            foreach (var target in draft.Concat(published).Concat(scheduled).Distinct())
            {
                if (target == item.Id || !collectionOf.ContainsKey(target))
                {
                    continue;
                }

                yield return new ItemReference(item.SiteId, item.Id, field.Id, target, draft.Contains(target), published.Contains(target), scheduled.Contains(target));
            }
        }
    }

    private static IEnumerable<ItemMediaUse> Media(Item item, CollectionSchemaDocument schema, Copies copy, HashSet<Guid> history)
    {
        var draft = MediaIdsOf(schema, copy.Draft);
        var live = copy.Published is null ? [] : MediaIdsOf(schema, copy.Published);
        var scheduled = copy.Captured is null ? [] : MediaIdsOf(schema, copy.Captured);

        return draft.Concat(live).Concat(scheduled).Concat(history).Distinct()
            .Select(id => new ItemMediaUse(item.SiteId, item.Id, id, draft.Contains(id), live.Contains(id), scheduled.Contains(id), history.Contains(id)));
    }

    /// <summary>Media used by kept versions other than the live one, read from the versions.</summary>
    private async Task<Dictionary<Guid, HashSet<Guid>>> HistoryMediaAsync(Guid siteId, IReadOnlyList<Item> items, CollectionSchemaDocument schema, CancellationToken cancellationToken)
    {
        if (!schema.Fields.Any(f => f.Type is FieldType.Image or FieldType.File))
        {
            return [];
        }

        var live = items.ToDictionary(i => i.Id, i => i.LiveVersion);
        var ids = live.Keys.ToList();
        var versions = await database.ForSite<ItemVersion>(siteId)
            .AsNoTracking()
            .Where(v => ids.Contains(v.ItemId))
            .Select(v => new { v.ItemId, v.Number, v.Values })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return versions
            .Where(v => v.Number != live[v.ItemId])
            .GroupBy(v => v.ItemId)
            .ToDictionary(g => g.Key, g => g.SelectMany(v => MediaIdsOf(schema, ItemValues.Read(v.Values))).ToHashSet());
    }

    /// <summary>The media history flags as they were, when this write did not change versions.</summary>
    private async Task<Dictionary<Guid, HashSet<Guid>>> KeptHistoryMediaAsync(Guid siteId, List<Guid> ids, CancellationToken cancellationToken) =>
        (await database.ForSite<ItemMediaUse>(siteId)
            .AsNoTracking()
            .Where(m => ids.Contains(m.ItemId) && m.InHistory)
            .Select(m => new { m.ItemId, m.MediaId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
        .GroupBy(m => m.ItemId)
        .ToDictionary(g => g.Key, g => g.Select(m => m.MediaId).ToHashSet());

    private static IEnumerable<JsonNode?> MediaValues(CollectionSchemaDocument schema, ItemValues values) =>
        schema.Fields.Where(f => f.Type is FieldType.Image or FieldType.File).Select(f => values.Get(f.Id));

    private static List<Guid> MediaIdsOf(CollectionSchemaDocument schema, ItemValues values) =>
        MediaValues(schema, values)
            .OfType<JsonObject>()
            .Where(v => v["mediaId"] is JsonValue id && id.TryGetValue<string>(out var text) && Guid.TryParse(text, out _))
            .Select(FieldConversion.MediaIdOf)
            .ToList();

    private static IEnumerable<Guid> ReferenceIds(CollectionSchemaDocument schema, ItemValues values) =>
        schema.Fields.Where(f => f.Type is FieldType.Reference or FieldType.MultiReference).SelectMany(f => IdsOf(f, values));

    /// <summary>The ids a reference field's value names, skipping anything that is not an id.</summary>
    private static List<Guid> IdsOf(Field field, ItemValues values) =>
        values.Get(field.Id) switch
        {
            JsonArray array => array.Select(n => n is JsonValue v && v.TryGetValue<string>(out var s) && Guid.TryParse(s, out var g) ? g : (Guid?)null)
                .OfType<Guid>()
                .ToList(),
            JsonValue v when v.TryGetValue<string>(out var s) && Guid.TryParse(s, out var g) => [g],
            _ => [],
        };

    private sealed record Copies(ItemValues Draft, ItemValues? Published, ItemValues? Captured)
    {
        public IEnumerable<ItemValues> All => new[] { Draft, Published, Captured }.OfType<ItemValues>();
    }

    /// <summary>The batch's media kinds and referenced items, already read, for <see cref="ItemValidation"/>.</summary>
    private sealed class BatchLookups(Func<Guid, MediaKind> kindOf, IReadOnlyDictionary<Guid, Guid> collectionOf)
        : IItemMediaLookup, IItemReferenceLookup
    {
        public Task<Func<Guid, MediaKind>> LookupAsync(Guid siteId, IEnumerable<JsonNode?> values, CancellationToken cancellationToken) =>
            Task.FromResult(kindOf);

        public Task<IReadOnlyDictionary<Guid, Guid>> CollectionsOfAsync(Guid siteId, IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken) =>
            Task.FromResult(collectionOf);
    }
}
