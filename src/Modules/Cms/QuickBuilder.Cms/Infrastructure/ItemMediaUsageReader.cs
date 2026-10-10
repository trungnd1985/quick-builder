using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Contracts.Media;
using QuickBuilder.Cms.Domain;

namespace QuickBuilder.Cms.Infrastructure;

/// <summary>
/// <see cref="IItemMediaUsageReader"/> over <c>item_media</c> (015 research IT-15). Reads the
/// site named by the caller, through <see cref="CmsDbContext.ForSite{T}"/>.
/// </summary>
internal sealed class ItemMediaUsageReader(CmsDbContext database) : IItemMediaUsageReader
{
    public async Task<IReadOnlyDictionary<Guid, ItemMediaUsageSummary>> SummariseAsync(
        Guid siteId, IReadOnlyCollection<Guid> mediaIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mediaIds);

        var result = mediaIds.Distinct().ToDictionary(id => id, _ => ItemMediaUsageSummary.None);
        if (result.Count == 0)
        {
            return result;
        }

        var ids = result.Keys.ToList();
        var uses = await database.ForSite<ItemMediaUse>(siteId)
            .AsNoTracking()
            .Where(m => ids.Contains(m.MediaId))
            .Select(m => new { m.MediaId, m.InDraft, m.InLive, m.InScheduled, m.InHistory })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var group in uses.GroupBy(u => u.MediaId))
        {
            result[group.Key] = new ItemMediaUsageSummary(
                group.Count(),
                group.Count(u => u.InDraft),
                group.Count(u => u.InLive),
                group.Count(u => u.InHistory && !u.InDraft && !u.InLive && !u.InScheduled));
        }

        return result;
    }

    public async Task<IReadOnlyList<ItemMediaUsagePlace>> ListAsync(Guid siteId, Guid mediaId, CancellationToken cancellationToken = default)
    {
        var rows = await database.ForSite<ItemMediaUse>(siteId)
            .AsNoTracking()
            .Where(m => m.MediaId == mediaId)
            .Join(database.ForSite<Item>(siteId), m => m.ItemId, i => i.Id, (m, i) => new
            {
                i.Id,
                i.Name,
                i.Slug,
                i.CollectionId,
                m.InDraft,
                m.InLive,
                m.InScheduled,
                m.InHistory,
            })
            .Join(database.ForSite<Collection>(siteId), r => r.CollectionId, c => c.Id, (r, c) => new { Row = r, CollectionName = c.Name })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .OrderBy(r => r.CollectionName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.Row.Name ?? r.Row.Slug, StringComparer.CurrentCultureIgnoreCase)
            .Select(r => new ItemMediaUsagePlace(
                r.Row.Id, r.Row.Name, r.Row.Slug, r.Row.CollectionId, r.CollectionName, r.Row.InDraft, r.Row.InLive, r.Row.InScheduled, r.Row.InHistory))
            .ToList();
    }

    public async Task<IReadOnlyList<Guid>> CollectionsShowingPublishedAsync(Guid siteId, Guid mediaId, CancellationToken cancellationToken = default) =>
        await database.ForSite<ItemMediaUse>(siteId)
            .AsNoTracking()
            .Where(m => m.MediaId == mediaId && m.InLive)
            .Join(database.ForSite<Item>(siteId), m => m.ItemId, i => i.Id, (m, i) => i.CollectionId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}
