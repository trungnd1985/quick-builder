using Microsoft.EntityFrameworkCore;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Pages.Domain;

namespace QuickBuilder.Pages.Infrastructure;

/// <summary>
/// <see cref="ICollectionUsageReader"/> over <c>pages.collection_uses</c> (016 research TB-13):
/// which pages and components use a collection, and how many read each of its fields.
/// </summary>
/// <remarks>
/// Asked by Cms on the administrative surface, which resolves no site, so every query names the
/// site explicitly. Titles are content: Cms renders them as text and never logs them.
/// </remarks>
internal sealed class CollectionUsageReader(PagesDbContext database) : ICollectionUsageReader
{
    public async Task<CollectionUses> ListUsesAsync(Guid siteId, Guid collectionId, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(limit);

        var rows = await database.ForSite<CollectionUsage>(siteId)
            .AsNoTracking()
            .Where(u => u.CollectionId == collectionId)
            .Select(u => new { u.SubjectKind, u.SubjectId, u.State })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (rows.Count == 0)
        {
            return CollectionUses.None;
        }

        var subjects = rows
            .GroupBy(r => (r.SubjectKind, r.SubjectId))
            .Select(g => (g.Key.SubjectKind, g.Key.SubjectId, States: (IReadOnlyList<string>)[.. CollectionUsageStates.All.Where(s => g.Any(r => r.State == s))]))
            .ToList();

        var pageIds = subjects.Where(s => s.SubjectKind == SubjectKinds.Page).Select(s => s.SubjectId).ToList();
        var componentIds = subjects.Where(s => s.SubjectKind == SubjectKinds.Component).Select(s => s.SubjectId).ToList();
        var pageTitles = await database.ForSite<Page>(siteId)
            .AsNoTracking()
            .Where(p => pageIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Title, cancellationToken)
            .ConfigureAwait(false);
        var componentNames = await database.ForSite<Component>(siteId)
            .AsNoTracking()
            .Where(c => componentIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken)
            .ConfigureAwait(false);

        var uses = subjects
            .Select(s => new CollectionUse(
                s.SubjectKind,
                s.SubjectId,
                (s.SubjectKind == SubjectKinds.Page ? pageTitles.GetValueOrDefault(s.SubjectId) : componentNames.GetValueOrDefault(s.SubjectId)) ?? string.Empty,
                s.States))
            .OrderBy(u => u.Kind, StringComparer.Ordinal)
            .ThenBy(u => u.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(u => u.Id)
            .ToList();

        return new CollectionUses([.. uses.Take(limit)], uses.Count);
    }

    public async Task<IReadOnlyDictionary<string, int>> CountFieldUsesAsync(
        Guid siteId, Guid collectionId, IReadOnlyCollection<string> fieldIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fieldIds);

        var counts = fieldIds.Distinct(StringComparer.Ordinal).ToDictionary(f => f, _ => 0, StringComparer.Ordinal);
        if (counts.Count == 0)
        {
            return counts;
        }

        var rows = await database.ForSite<CollectionUsage>(siteId)
            .AsNoTracking()
            .Where(u => u.CollectionId == collectionId)
            .Select(u => new { u.SubjectKind, u.SubjectId, u.FieldIds })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // A page that reads a field in its draft and its live version is one page.
        foreach (var field in counts.Keys.ToList())
        {
            counts[field] = rows.Where(r => r.FieldIds.Contains(field)).Select(r => (r.SubjectKind, r.SubjectId)).Distinct().Count();
        }

        return counts;
    }
}
