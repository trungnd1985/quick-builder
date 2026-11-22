using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Contracts.Publishing;
using QuickBuilder.Cms.Domain;

namespace QuickBuilder.Cms.Infrastructure;

/// <summary>
/// <see cref="IPreviewItemReader"/> over the read-only preview context (022 research PV-02).
/// </summary>
/// <remarks>
/// A draft or version is handed over as a <see cref="PublishedItem"/>, named by its collection's
/// title field and with its values at the current version, exactly as published items are
/// (016 research TB-08), so bindings render it unchanged (spec FR-002). Values, names and slugs
/// are content: never logged.
/// </remarks>
internal sealed class PreviewItemReader(PreviewReadDbContext database, Schema.CollectionSchemaUpgrades upgrades) : IPreviewItemReader
{
    public async Task<PreviewItem?> GetDraftAsync(Guid siteId, Guid itemId, CancellationToken cancellationToken = default)
    {
        using var row = await database.Items
            .Where(i => i.SiteId == siteId && i.Id == itemId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        var titleField = await TitleFieldAsync(siteId, row.CollectionId, cancellationToken).ConfigureAwait(false);
        if (titleField is null)
        {
            // The collection is gone, or its schema cannot be used: nothing can show the item.
            return null;
        }

        var scheduled = await database.Schedules
            .Where(s => s.SiteId == siteId && s.ItemId == itemId
                && s.State == ItemScheduleStates.Pending && s.Action == ItemScheduleActions.Publish)
            .OrderBy(s => s.DueAtUtc)
            .Select(s => (DateTimeOffset?)s.DueAtUtc)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var values = Current(row.Id, row.Draft.RootElement);
        var item = Item(row.Id, row.CollectionId, row.Slug, titleField.Value, values, row.FirstPublishedAtUtc, row.DraftChangedAtUtc);
        return new PreviewItem(item, row.DraftChangedAtUtc, row.DraftChangedBy, row.Attention is not null, scheduled, row.PublishedSlug is not null);
    }

    public async Task<PreviewItemVersion?> GetVersionAsync(Guid siteId, Guid itemId, int number, CancellationToken cancellationToken = default)
    {
        var found = await (
                from item in database.Items
                where item.SiteId == siteId && item.Id == itemId
                join row in database.Versions on item.Id equals row.ItemId
                where row.Number == number
                select new { item.CollectionId, item.FirstPublishedAtUtc, Version = row })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (found is null)
        {
            return null;
        }

        using var version = found.Version;
        var titleField = await TitleFieldAsync(siteId, found.CollectionId, cancellationToken).ConfigureAwait(false);
        if (titleField is null)
        {
            return null;
        }

        var values = Current(itemId, version.Values.RootElement);
        var shown = Item(itemId, found.CollectionId, version.Slug, titleField.Value, values, found.FirstPublishedAtUtc, version.PublishedAtUtc);
        return new PreviewItemVersion(shown, version.Number, version.PublishedAtUtc, version.PublishedBy);
    }

    public async Task<IReadOnlyDictionary<Guid, PreviewItemName>> GetNamesAsync(
        Guid siteId, IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        if (itemIds.Count == 0)
        {
            return new Dictionary<Guid, PreviewItemName>();
        }

        var ids = itemIds.Distinct().ToList();
        var rows = await database.Items
            .Where(i => i.SiteId == siteId && ids.Contains(i.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var result = new Dictionary<Guid, PreviewItemName>(rows.Count);
        var titleFields = new Dictionary<Guid, string?>();
        foreach (var row in rows)
        {
            using (row)
            {
                if (!titleFields.TryGetValue(row.CollectionId, out var titleField))
                {
                    titleField = (await TitleFieldAsync(siteId, row.CollectionId, cancellationToken).ConfigureAwait(false))?.Value;
                    titleFields[row.CollectionId] = titleField;
                }

                result[row.Id] = new PreviewItemName(row.CollectionId, NameOf(row.Draft.RootElement, titleField));
            }
        }

        return result;
    }

    /// <summary>
    /// The collection's title field id (possibly none), or null when the collection is not
    /// readable or its schema cannot be used.
    /// </summary>
    private async Task<TitleField?> TitleFieldAsync(Guid siteId, Guid collectionId, CancellationToken cancellationToken)
    {
        var schema = await database.Collections
            .Where(c => c.SiteId == siteId && c.Id == collectionId)
            .Select(c => c.Schema)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (schema is null)
        {
            return null;
        }

        using var parsed = JsonDocument.Parse(schema);
        if (upgrades.ForRendering("collection", collectionId, parsed.RootElement) is not { } current)
        {
            return null;
        }

        return new TitleField(
            current.TryGetProperty("titleFieldId", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null);
    }

    // At the current version, detached from the row's buffer; an empty document when the values
    // cannot be upgraded, so the item renders as a published one that failed would (019 RL-19).
    private JsonElement Current(Guid itemId, JsonElement stored) =>
        upgrades.ForRendering("item", itemId, stored) is { } current ? current.Clone() : default;

    private static PublishedItem Item(
        Guid id, Guid collectionId, string slug, string? titleField, JsonElement values, DateTimeOffset? firstPublished, DateTimeOffset last) =>
        new(id, collectionId, slug, NameOf(values, titleField), firstPublished ?? last, last, values);

    private static string? NameOf(JsonElement values, string? titleField) =>
        titleField is not null
        && values.ValueKind == JsonValueKind.Object
        && values.TryGetProperty("values", out var fields)
        && fields.TryGetProperty(titleField, out var title)
        && title.ValueKind == JsonValueKind.String
            ? title.GetString()
            : null;

    private sealed record TitleField(string? Value);
}
