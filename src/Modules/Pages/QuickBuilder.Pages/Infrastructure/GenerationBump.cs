using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Contracts;
using QuickBuilder.Pages.Domain;

namespace QuickBuilder.Pages.Infrastructure;

/// <summary>
/// Moves on the render generation of exactly the pages a change affects (012 research PR-04,
/// PR-05, data-model §3.2).
/// </summary>
/// <remarks>
/// Rendering keys each cached page by its generation, read from the database on every
/// request. Incrementing it here, in the transaction of the change, is therefore all it takes
/// for every instance to serve the new content at its next request — no cache is ever told
/// anything. The pages affected by a target are those that depend on it directly, those that
/// show a component that depends on it, and (024) those that show a menu pointing at it, also
/// through a component; menus and components do not nest, so three levels is all.
/// <para>
/// One set-based <c>UPDATE</c> through EF Core, with the site named explicitly: the admin
/// surface has no resolved site, so the query filter would match nothing, and the explicit
/// predicate keeps one site's change from ever touching another's pages
/// (<c>GenerationBumpSiteFilterTests</c>).
/// </para>
/// </remarks>
internal sealed class GenerationBump(PagesDbContext database, ICollectionSchemaReader collections, SitemapGeneration sitemap)
{
    /// <summary>The site's sitemap generation (017 research SE-15), moved with the changes it lists.</summary>
    public SitemapGeneration Sitemap => sitemap;

    /// <summary>
    /// Increments the generation of the live pages that list <paramref name="collectionIds"/>, or
    /// a collection whose items can show theirs through a reference, directly or through a
    /// component; with <paramref name="includeTemplates"/>, also of their templates, which
    /// refreshes every item page (016 research TB-10).
    /// </summary>
    /// <remarks>
    /// "A collection whose items can show theirs" is one with a reference field targeting it: a
    /// card may show "Author → Name". Refreshing a list page that does not show that reference
    /// costs one render; missing one would show stale content.
    /// </remarks>
    public async Task<int> BumpForCollectionsAsync(
        Guid siteId, IReadOnlyCollection<Guid> collectionIds, bool includeTemplates, Guid? exceptPageId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(collectionIds);

        var affected = new HashSet<Guid>(collectionIds);
        foreach (var collectionId in collectionIds)
        {
            affected.UnionWith(await collections.CollectionsReferencingAsync(siteId, collectionId, cancellationToken).ConfigureAwait(false));
        }

        var targets = affected.Select(id => (DependencyKinds.Collection, id)).ToList();
        if (includeTemplates)
        {
            targets.AddRange(affected.Select(id => (DependencyKinds.Template, id)));
        }

        return await BumpForTargetsAsync(siteId, targets, exceptPageId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Increments the generation of every live page that depends on any of <paramref name="targets"/>,
    /// except <paramref name="exceptPageId"/> (the subject, whose own row the caller saves).
    /// </summary>
    /// <returns>How many pages it moved on.</returns>
    public async Task<int> BumpForTargetsAsync(
        Guid siteId,
        IReadOnlyCollection<(string Kind, Guid Id)> targets,
        Guid? exceptPageId,
        CancellationToken cancellationToken)
    {
        if (targets.Count == 0)
        {
            return 0;
        }

        var pageIds = await AffectedPagesAsync(siteId, targets, cancellationToken).ConfigureAwait(false);
        if (exceptPageId is { } except)
        {
            pageIds.Remove(except);
        }

        return await BumpPagesAsync(siteId, pageIds, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Increments the generation of the given pages' publications, when they exist.</summary>
    public Task<int> BumpPagesAsync(Guid siteId, IReadOnlyCollection<Guid> pageIds, CancellationToken cancellationToken)
    {
        if (pageIds.Count == 0)
        {
            return Task.FromResult(0);
        }

        return database.ForSite<Publication>(siteId)
            .Where(p => p.SubjectKind == SubjectKinds.Page && pageIds.Contains(p.SubjectId))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Generation, p => p.Generation + 1), cancellationToken);
    }

    /// <summary>
    /// Increments the generation of every live page of the site: for what every page shows
    /// without referencing it, such as the site's default sharing image (017 research SE-20).
    /// Bounded by the site's 2,000 pages.
    /// </summary>
    public Task<int> BumpAllLivePagesAsync(Guid siteId, CancellationToken cancellationToken) =>
        database.ForSite<Publication>(siteId)
            .Where(p => p.SubjectKind == SubjectKinds.Page && p.LiveVersionId != null)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Generation, p => p.Generation + 1), cancellationToken);

    /// <summary>
    /// The pages that depend on any target, directly, through a menu they show, or through a
    /// component (024 data-model §3). Menus do not nest and components do not nest, so the walk
    /// is three queries at most: targets → pages, components, menus; menus → pages, components;
    /// components → pages.
    /// </summary>
    public async Task<HashSet<Guid>> AffectedPagesAsync(
        Guid siteId, IReadOnlyCollection<(string Kind, Guid Id)> targets, CancellationToken cancellationToken)
    {
        var dependents = await DependentsAsync(siteId, targets, cancellationToken).ConfigureAwait(false);

        // 024 NV-07: a page or item a menu points at reaches the pages and components showing the menu.
        var menus = dependents.Where(d => d.Kind == SubjectKinds.Menu).Select(d => (DependencyKinds.Menu, d.Id)).ToList();
        if (menus.Count > 0)
        {
            dependents.AddRange(await DependentsAsync(siteId, menus, cancellationToken).ConfigureAwait(false));
        }

        var pages = dependents.Where(d => d.Kind == SubjectKinds.Page).Select(d => d.Id).ToHashSet();
        var components = dependents.Where(d => d.Kind == SubjectKinds.Component).Select(d => (DependencyKinds.Component, d.Id)).Distinct().ToList();

        if (components.Count > 0)
        {
            var throughComponents = await DependentsAsync(siteId, components, cancellationToken).ConfigureAwait(false);
            pages.UnionWith(throughComponents.Where(d => d.Kind == SubjectKinds.Page).Select(d => d.Id));
        }

        return pages;
    }

    private async Task<List<(string Kind, Guid Id)>> DependentsAsync(
        Guid siteId, IReadOnlyCollection<(string Kind, Guid Id)> targets, CancellationToken cancellationToken)
    {
        var result = new List<(string, Guid)>();

        // Grouped by kind so each query is an IN list on the reverse index.
        foreach (var group in targets.GroupBy(t => t.Kind, StringComparer.Ordinal))
        {
            var kind = group.Key;
            var ids = group.Select(t => t.Id).Distinct().ToList();

            var rows = await database.ForSite<LiveDependency>(siteId)
                .Where(d => d.TargetKind == kind && ids.Contains(d.TargetId))
                .Select(d => new { d.SubjectKind, d.SubjectId })
                .Distinct()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            result.AddRange(rows.Select(r => (r.SubjectKind, r.SubjectId)));
        }

        return result;
    }
}
