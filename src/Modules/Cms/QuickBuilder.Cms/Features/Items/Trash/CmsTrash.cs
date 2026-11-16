using System.Globalization;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Trash;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Cms.Features.Items.Trash;

/// <summary>A filter over the item trash (021 FR-010, TR-19).</summary>
internal sealed record TrashQueryOptions(string? Search, Guid? TrashedBy, DateTimeOffset? From, DateTimeOffset? To, Guid? CollectionId, string? After, int Limit);

/// <summary>
/// What the Cms trash slices share: reading trashed items, describing them, and planning a
/// restore (021 TR-10, TR-19).
/// </summary>
/// <remarks>
/// Restore authority mirrors delete: <c>cms.items.edit</c> (FR-013). Every read names its
/// reason (TR-03).
/// </remarks>
internal sealed class CmsTrash(
    CmsDbContext database,
    CmsAccess access,
    IAccessEvaluator capabilities,
    SlugAvailability slugs,
    ItemValidation validation,
    TrashRetention retention)
{
    internal const int PageSize = 50;
    private const string Reason = "The site's item trash: list, preview and restore (021 TR-01).";

    internal IQueryable<Item> TrashedItems(Guid siteId) => database.ForSite<Item>(siteId).OnlyTrash(Reason);

    internal Task<bool> MayAsync(string capability, Guid siteId, CancellationToken cancellationToken) =>
        capabilities.HasCapabilityAsync(capability, siteId, cancellationToken);

    internal async Task<TrashListResponse> ListAsync(Guid siteId, TrashQueryOptions options, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(options.Limit, 1, PageSize);
        var query = TrashedItems(siteId);

        if (!string.IsNullOrWhiteSpace(options.Search))
        {
            var pattern = "%" + options.Search.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            query = query.Where(i => EF.Functions.ILike(i.Name ?? string.Empty, pattern) || EF.Functions.ILike(i.Slug, pattern));
        }

        if (options.TrashedBy is { } by)
        {
            query = query.Where(i => i.TrashedBy == by);
        }

        if (options.From is { } from)
        {
            query = query.Where(i => i.TrashedAtUtc >= from);
        }

        if (options.To is { } to)
        {
            query = query.Where(i => i.TrashedAtUtc <= to);
        }

        if (options.CollectionId is { } collection)
        {
            query = query.Where(i => i.CollectionId == collection);
        }

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        if (Cursor.Parse(options.After) is { } c)
        {
            query = query.Where(i => i.TrashedAtUtc < c.At || (i.TrashedAtUtc == c.At && i.Id.CompareTo(c.Id) < 0));
        }

        var rows = await query
            .OrderByDescending(i => i.TrashedAtUtc).ThenByDescending(i => i.Id)
            .Take(limit + 1)
            .Select(i => new TrashRow(i.Id, i.CollectionId, i.Name, i.Slug, i.TrashedAtUtc!.Value, i.TrashedBy!.Value, i.TrashedWasLive == true, i.TrashedHadSchedule == true))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var more = rows.Count > limit;
        rows = [.. rows.Take(limit)];
        var entries = await DescribeAsync(siteId, rows, cancellationToken).ConfigureAwait(false);
        return new TrashListResponse(entries, more ? new Cursor(rows[^1].TrashedAtUtc, rows[^1].Id).ToString() : null, total);
    }

    internal async Task<Result<TrashEntryDetailResponse>> GetAsync(Guid siteId, Guid itemId, CancellationToken cancellationToken)
    {
        var item = await TrashedItems(siteId).AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return Result.Failure<TrashEntryDetailResponse>(CmsErrors.NotInTrash);
        }

        using (item)
        {
            var row = new TrashRow(item.Id, item.CollectionId, item.Name, item.Slug, item.TrashedAtUtc!.Value, item.TrashedBy!.Value, item.TrashedWasLive == true, item.TrashedHadSchedule == true);
            var entries = await DescribeAsync(siteId, [row], cancellationToken).ConfigureAwait(false);
            return Result.Success(new TrashEntryDetailResponse(entries[0], item.Draft.RootElement.GetRawText()));
        }
    }

    /// <summary>What restoring the item will do: its slug, or the next free one (FR-017).</summary>
    internal async Task<RestorePlanResponse> PlanAsync(Item item, CancellationToken cancellationToken)
    {
        var slug = await slugs.NextFreeAsync(item.SiteId, item.CollectionId, item.Slug, item.Id, cancellationToken).ConfigureAwait(false);
        var canPublish = await MayAsync(CmsCapabilities.ItemsPublish, item.SiteId, cancellationToken).ConfigureAwait(false)
                         && await LastLiveVersionAsync(item.SiteId, item.Id, cancellationToken).ConfigureAwait(false) is not null;
        return new RestorePlanResponse(
            "original",
            item.CollectionId,
            slug,
            item.Slug,
            !string.Equals(slug, item.Slug, StringComparison.Ordinal),
            await SchemaIssuesAsync(item, cancellationToken).ConfigureAwait(false),
            item.TrashedWasLive == true,
            item.TrashedHadSchedule == true,
            canPublish);
    }

    /// <summary>The fields it would need attention on under the collection's schema today (FR-018).</summary>
    private async Task<IReadOnlyList<string>> SchemaIssuesAsync(Item item, CancellationToken cancellationToken)
    {
        var stored = await database.ForSite<Collection>(item.SiteId)
            .Where(c => c.Id == item.CollectionId)
            .Select(c => c.Schema)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
        var schema = CollectionSchemaDocument.Read(stored);
        var report = await validation.ValidateAsync(item.SiteId, item.Id, schema, item.Slug, item.DraftValues(), cancellationToken).ConfigureAwait(false);

        // The name of each field, as the member knows it; a field that no longer exists is simply not shown.
        return [.. report.Problems.Select(p => schema.Find(p.FieldId)?.Name).OfType<string>().Distinct(StringComparer.Ordinal)];
    }

    /// <summary>The number of the last version that was live, or null when it never was (TR-11).</summary>
    internal async Task<int?> LastLiveVersionAsync(Guid siteId, Guid itemId, CancellationToken cancellationToken) =>
        await database.ForSite<ItemVersion>(siteId)
            .Where(v => v.ItemId == itemId)
            .OrderByDescending(v => v.Number)
            .Select(v => (int?)v.Number)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    private async Task<List<TrashEntryResponse>> DescribeAsync(Guid siteId, IReadOnlyList<TrashRow> rows, CancellationToken cancellationToken)
    {
        var canRestore = await MayAsync(CmsCapabilities.ItemsEdit, siteId, cancellationToken).ConfigureAwait(false);
        var canPurge = await MayAsync(CmsCapabilities.TrashPurge, siteId, cancellationToken).ConfigureAwait(false);
        var names = await access.NamesAsync(rows.Select(r => r.TrashedBy), cancellationToken).ConfigureAwait(false);
        var collectionIds = rows.Select(r => r.CollectionId).Distinct().ToList();
        var collections = await database.ForSite<Collection>(siteId)
            .Where(c => collectionIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name })
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken)
            .ConfigureAwait(false);

        return [.. rows.Select(r => new TrashEntryResponse(
            TrashKinds.Item,
            r.Id,
            r.Name ?? r.Slug,
            r.Slug,
            new TrashPlaceResponse(r.CollectionId, collections.GetValueOrDefault(r.CollectionId)),
            r.TrashedAtUtc,
            new TrashActorResponse(r.TrashedBy, names.GetValueOrDefault(r.TrashedBy) ?? string.Empty),
            retention.PurgeOn(r.TrashedAtUtc),
            r.WasLive,
            r.HadSchedule,
            canRestore,
            canPurge))];
    }

    private sealed record TrashRow(
        Guid Id, Guid CollectionId, string? Name, string Slug, DateTimeOffset TrashedAtUtc, Guid TrashedBy, bool WasLive, bool HadSchedule);

    /// <summary>A keyset position: the last entry's trash time and id.</summary>
    private readonly record struct Cursor(DateTimeOffset At, Guid Id)
    {
        public static Cursor? Parse(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            var parts = text.Split('_');
            return parts.Length == 2
                   && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                   && Guid.TryParse(parts[1], out var id)
                ? new Cursor(new DateTimeOffset(ticks, TimeSpan.Zero), id)
                : null;
        }

        public override string ToString() => $"{At.UtcTicks.ToString(CultureInfo.InvariantCulture)}_{Id:N}";
    }
}
