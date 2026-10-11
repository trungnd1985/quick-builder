using Microsoft.EntityFrameworkCore;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Pages.Domain;

namespace QuickBuilder.Pages.Infrastructure;

/// <summary>
/// <see cref="ITemplateReader"/> for other modules' admin-side questions (016 research TB-12).
/// </summary>
/// <remarks>
/// Reads through <see cref="PagesDbContext.ForSite{T}"/>: callers are on the administrative
/// surface, which resolves no site, and name the site explicitly.
/// </remarks>
internal sealed class TemplateReader(PagesDbContext database) : ITemplateReader
{
    public async Task<TemplateInfo?> GetTemplateAsync(Guid siteId, Guid collectionId, CancellationToken cancellationToken = default) =>
        (await GetTemplatesAsync(siteId, [collectionId], cancellationToken).ConfigureAwait(false)).GetValueOrDefault(collectionId);

    public async Task<IReadOnlyDictionary<Guid, TemplateInfo>> GetTemplatesAsync(
        Guid siteId, IReadOnlyCollection<Guid> collectionIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collectionIds);

        if (collectionIds.Count == 0)
        {
            return new Dictionary<Guid, TemplateInfo>();
        }

        var ids = collectionIds.Distinct().ToList();
        var templates = await (
                from page in database.ForSite<Page>(siteId)
                where page.TemplateCollectionId != null && ids.Contains(page.TemplateCollectionId.Value)
                from publication in database.ForSite<Publication>(siteId)
                    .Where(p => p.SubjectKind == SubjectKinds.Page && p.SubjectId == page.Id)
                    .DefaultIfEmpty()
                select new
                {
                    CollectionId = page.TemplateCollectionId!.Value,
                    page.Id,
                    page.Path,
                    IsLive = publication != null && publication.LiveVersionId != null,
                })
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return templates.ToDictionary(t => t.CollectionId, t => new TemplateInfo(t.Id, t.Path, t.IsLive));
    }

    public Task<bool> LivePageAtAsync(Guid siteId, string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);

        var lowered = path.ToLowerInvariant();
        return (from page in database.ForSite<Page>(siteId)
                where page.Path == lowered && page.TemplateCollectionId == null
                from publication in database.ForSite<Publication>(siteId)
                    .Where(p => p.SubjectKind == SubjectKinds.Page && p.SubjectId == page.Id && p.LiveVersionId != null)
                select page.Id)
            .AnyAsync(cancellationToken);
    }
}
