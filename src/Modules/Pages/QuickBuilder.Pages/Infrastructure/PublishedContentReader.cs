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
    // 016 research TB-02: a template has no address of its own, so it is never found by path.
    private static readonly Func<PublishedReadDbContext, Guid, string, CancellationToken, Task<LivePageRef?>> LiveByPath =
        EF.CompileAsyncQuery((PublishedReadDbContext db, Guid siteId, string path, CancellationToken _) =>
            (from page in db.Pages
             where page.SiteId == siteId && page.Path == path && page.TemplateCollectionId == null
             join publication in db.Publications
                 on new { Kind = SubjectKinds.Page, Id = page.Id } equals new { Kind = publication.SubjectKind, Id = publication.SubjectId }
             where publication.LiveVersionId != null
             select new LivePageRef(page.Id, publication.Generation, publication.PageParamNames))
            .FirstOrDefault());

    // 016 research TB-03: the template under a parent is the one whose path is the pattern.
    private static readonly Func<PublishedReadDbContext, Guid, string, CancellationToken, Task<LiveTemplateRef?>> LiveTemplateByPattern =
        EF.CompileAsyncQuery((PublishedReadDbContext db, Guid siteId, string pattern, CancellationToken _) =>
            (from page in db.Pages
             where page.SiteId == siteId && page.Path == pattern && page.TemplateCollectionId != null
             join publication in db.Publications
                 on new { Kind = SubjectKinds.Page, Id = page.Id } equals new { Kind = publication.SubjectKind, Id = publication.SubjectId }
             where publication.LiveVersionId != null
             select new LiveTemplateRef(page.Id, publication.Generation, page.TemplateCollectionId!.Value, publication.PageParamNames))
            .FirstOrDefault());

    // 016 FR-027: the page parameters of the live components a live page shows, so a list in a
    // header or footer pages like one in the page itself.
    private static readonly Func<PublishedReadDbContext, Guid, Guid, IAsyncEnumerable<string[]>> ComponentParams =
        EF.CompileAsyncQuery((PublishedReadDbContext db, Guid siteId, Guid pageId) =>
            from dependency in db.Dependencies
            where dependency.SiteId == siteId
                  && dependency.SubjectKind == SubjectKinds.Page
                  && dependency.SubjectId == pageId
                  && dependency.TargetKind == DependencyKinds.Component
            join publication in db.Publications
                on new { Kind = SubjectKinds.Component, Id = dependency.TargetId } equals new { Kind = publication.SubjectKind, Id = publication.SubjectId }
            where publication.LiveVersionId != null && publication.PageParamNames.Length > 0
            select publication.PageParamNames);

    public async Task<LivePageRef?> FindLiveByPathAsync(Guid siteId, string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);

        var live = await LiveByPath(database, siteId, path, cancellationToken).ConfigureAwait(false);
        return live is null
            ? null
            : live with { PageParamNames = await WithComponentParamsAsync(siteId, live.PageId, live.Params, cancellationToken).ConfigureAwait(false) };
    }

    public async Task<LiveTemplateRef?> FindLiveTemplateAsync(Guid siteId, string parentPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parentPath);

        // A template's pattern always has a fixed segment before {slug}: the tree refuses a
        // template at the top level or under the home page, whose address is "/" (TB-02).
        var template = await LiveTemplateByPattern(database, siteId, TemplatePattern(parentPath), cancellationToken).ConfigureAwait(false);
        return template is null
            ? null
            : template with { PageParamNames = await WithComponentParamsAsync(siteId, template.PageId, template.Params, cancellationToken).ConfigureAwait(false) };
    }

    /// <summary>The page's own page parameters and those of the live components it shows, without repeats.</summary>
    private async Task<IReadOnlyList<string>> WithComponentParamsAsync(
        Guid siteId, Guid pageId, IReadOnlyList<string> own, CancellationToken cancellationToken)
    {
        var names = new SortedSet<string>(own, StringComparer.Ordinal);
        await foreach (var component in ComponentParams(database, siteId, pageId).WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            names.UnionWith(component);
        }

        return names.Count == own.Count ? own : [.. names];
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetLiveTemplatePatternsAsync(
        Guid siteId, IReadOnlyCollection<Guid> collectionIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collectionIds);

        if (collectionIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var templates = await (
                from page in database.Pages
                where page.SiteId == siteId && page.TemplateCollectionId != null && collectionIds.Contains(page.TemplateCollectionId.Value)
                join publication in database.Publications
                    on new { Kind = SubjectKinds.Page, Id = page.Id } equals new { Kind = publication.SubjectKind, Id = publication.SubjectId }
                where publication.LiveVersionId != null
                select new { CollectionId = page.TemplateCollectionId!.Value, page.Path })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return templates.ToDictionary(t => t.CollectionId, t => t.Path);
    }

    /// <summary>The pattern of a template under <paramref name="parentPath"/>: the parent's address, then <c>/{slug}</c>.</summary>
    internal static string TemplatePattern(string parentPath) =>
        (parentPath == "/" ? string.Empty : parentPath) + "/" + QuickBuilder.Pages.Contracts.TemplateInfo.SlugPlaceholder;

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
                select new
                {
                    page.Id,
                    page.Path,
                    // 016 research TB-02: a link to a template page itself goes nowhere, so it is
                    // never live as a link target and renders as plain text (012 FR-026).
                    IsLive = publication != null && publication.LiveVersionId != null && page.TemplateCollectionId == null,
                    IsTemplate = page.TemplateCollectionId != null,
                    // 024 NV-09: the title visitors saw last, for menu entries without their own
                    // label. The live version is always the newest, since a rollback publishes a
                    // new number; after an unpublish, the newest is the one that was live.
                    LastTitle = database.Versions
                        .Where(v => v.SiteId == siteId && v.SubjectKind == SubjectKinds.Page && v.SubjectId == page.Id)
                        .OrderByDescending(v => v.Number)
                        .Select(v => v.Title)
                        .FirstOrDefault(),
                })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return targets.ToDictionary(t => t.Id, t => new PageLinkTarget(t.Path, t.IsLive, t.IsTemplate ? null : t.LastTitle));
    }

    public async Task<IReadOnlyDictionary<Guid, LiveMenu>> GetLiveMenusAsync(
        Guid siteId, IReadOnlyCollection<Guid> menuIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(menuIds);

        if (menuIds.Count == 0)
        {
            return new Dictionary<Guid, LiveMenu>();
        }

        var versions = await LiveVersions(siteId, SubjectKinds.Menu, menuIds)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // 024 NV-05: a menu version's title is the menu's name when it was published.
        return versions.ToDictionary(
            v => v.SubjectId,
            v => new LiveMenu(v.Row.SchemaVersion, Parse(v.Row.Document), v.Row.Title ?? string.Empty));
    }

    public Task<bool> IsHomePageLiveAsync(Guid siteId, CancellationToken cancellationToken = default) =>
        (from page in database.Pages
         where page.SiteId == siteId && page.IsHome
         join publication in database.Publications
             on new { Kind = SubjectKinds.Page, Id = page.Id } equals new { Kind = publication.SubjectKind, Id = publication.SubjectId }
         where publication.LiveVersionId != null
         select page.Id)
        .AnyAsync(cancellationToken);

    /// <summary>Breadcrumb ancestors; built with structured data (017 tasks T099).</summary>
    public async Task<IReadOnlyList<LiveAncestor>> GetLiveAncestorsAsync(Guid siteId, Guid pageId, CancellationToken cancellationToken = default)
    {
        var path = await database.Pages
            .Where(p => p.SiteId == siteId && p.Id == pageId)
            .Select(p => p.Path)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (path is null || path == "/")
        {
            return [];
        }

        // Every address above it: /a/b/c (or the pattern /a/b/{slug}) has /a and /a/b above it.
        var prefixes = new List<string>();
        for (var end = path.IndexOf('/', 1); end > 0; end = path.IndexOf('/', end + 1))
        {
            prefixes.Add(path[..end]);
        }

        var ancestors = await (
                from page in database.Pages
                where page.SiteId == siteId && page.TemplateCollectionId == null && (page.IsHome || prefixes.Contains(page.Path))
                join publication in database.Publications
                    on new { Kind = SubjectKinds.Page, Id = page.Id } equals new { Kind = publication.SubjectKind, Id = publication.SubjectId }
                where publication.LiveVersionId != null
                join version in database.Versions on publication.LiveVersionId equals version.Id
                select new { page.Id, page.Path, page.IsHome, version.Title })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. ancestors
            .Where(a => a.Id != pageId)
            .OrderBy(a => a.IsHome ? 0 : 1)
            .ThenBy(a => a.Path.Length)
            .Select(a => new LiveAncestor(a.Id, a.Path, a.Title ?? string.Empty, a.IsHome))];
    }

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
