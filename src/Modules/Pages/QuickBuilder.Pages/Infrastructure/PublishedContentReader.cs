using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Pages.Domain;

namespace QuickBuilder.Pages.Infrastructure;

/// <summary>
/// <see cref="IPublishedContentReader"/> over the read-only context (012 research PR-06).
/// </summary>
/// <remarks>
/// <see cref="FindLiveByPathAsync"/> runs on every public request, so it is a compiled query
/// on <c>ux_pages_site_path</c> joined to the publication's primary key.
/// </remarks>
internal sealed class PublishedContentReader(PublishedReadDbContext database) : IPublishedContentReader
{
    private static readonly Func<PublishedReadDbContext, Guid, string, CancellationToken, Task<LivePageRef?>> LiveByPath =
        EF.CompileAsyncQuery((PublishedReadDbContext db, Guid siteId, string path, CancellationToken _) =>
            (from page in db.Pages
             where page.SiteId == siteId && page.Path == path
             join publication in db.Publications
                 on new { Kind = SubjectKinds.Page, Id = page.Id } equals new { Kind = publication.SubjectKind, Id = publication.SubjectId }
             where publication.LiveVersionId != null
             select new LivePageRef(page.Id, publication.Generation))
            .FirstOrDefault());

    public Task<LivePageRef?> FindLiveByPathAsync(Guid siteId, string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        return LiveByPath(database, siteId, path, cancellationToken);
    }

    public async Task<LivePageVersion?> GetLivePageAsync(Guid siteId, Guid pageId, CancellationToken cancellationToken = default)
    {
        var version = await LiveVersions(siteId, SubjectKinds.Page, [pageId])
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return version is null
            ? null
            : new LivePageVersion(version.Row.SchemaVersion, Parse(version.Row.Document), version.Row.Title ?? string.Empty);
    }

    public async Task<IReadOnlyDictionary<Guid, LiveDocument>> GetLiveComponentsAsync(
        Guid siteId, IReadOnlyCollection<Guid> componentIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(componentIds);

        if (componentIds.Count == 0)
        {
            return new Dictionary<Guid, LiveDocument>();
        }

        var versions = await LiveVersions(siteId, SubjectKinds.Component, componentIds)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return versions.ToDictionary(v => v.SubjectId, v => new LiveDocument(v.Row.SchemaVersion, Parse(v.Row.Document)));
    }

    public async Task<IReadOnlyDictionary<Guid, PageLinkTarget>> GetLinkTargetsAsync(
        Guid siteId, IReadOnlyCollection<Guid> pageIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pageIds);

        if (pageIds.Count == 0)
        {
            return new Dictionary<Guid, PageLinkTarget>();
        }

        var targets = await (
                from page in database.Pages
                where page.SiteId == siteId && pageIds.Contains(page.Id)
                from publication in database.Publications
                    .Where(p => p.SubjectKind == SubjectKinds.Page && p.SubjectId == page.Id)
                    .DefaultIfEmpty()
                select new { page.Id, page.Path, IsLive = publication != null && publication.LiveVersionId != null })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return targets.ToDictionary(t => t.Id, t => new PageLinkTarget(t.Path, t.IsLive));
    }

    public Task<bool> IsHomePageLiveAsync(Guid siteId, CancellationToken cancellationToken = default) =>
        (from page in database.Pages
         where page.SiteId == siteId && page.IsHome
         join publication in database.Publications
             on new { Kind = SubjectKinds.Page, Id = page.Id } equals new { Kind = publication.SubjectKind, Id = publication.SubjectId }
         where publication.LiveVersionId != null
         select page.Id)
        .AnyAsync(cancellationToken);

    private IQueryable<LiveVersion> LiveVersions(Guid siteId, string subjectKind, IReadOnlyCollection<Guid> subjectIds) =>
        from publication in database.Publications
        where publication.SiteId == siteId
              && publication.SubjectKind == subjectKind
              && subjectIds.Contains(publication.SubjectId)
              && publication.LiveVersionId != null
        join version in database.Versions on publication.LiveVersionId equals version.Id
        select new LiveVersion(publication.SubjectId, version);

    // Parsed once per read and detached from the pooled buffer, so the caller may keep it.
    private static JsonElement Parse(string document)
    {
        using var parsed = JsonDocument.Parse(document);
        return parsed.RootElement.Clone();
    }

    private sealed record LiveVersion(Guid SubjectId, VersionRow Row);
}
