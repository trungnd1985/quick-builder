using Microsoft.EntityFrameworkCore;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Infrastructure;

/// <summary>
/// Contract §3 rule 21 (016 FR-027): the paginated lists of a page and of the shared components
/// it shows read distinct page parameters, or two of them would turn pages together.
/// </summary>
/// <remarks>
/// Rule 19 already keeps one document's lists apart; this rule needs other documents, so, like
/// rules 6 and 14, it is checked by the server after the schema rules. Draft saves compare
/// drafts (what members see in the editor), and publishes compare live versions (what visitors
/// get). A component checks every page that shows it, and the other components on those pages.
/// The refusal points into the document being saved or published.
/// </remarks>
internal sealed class PageParamRules(PagesDbContext database)
{
    /// <summary>The rule a clash breaks.</summary>
    public const string Rule = "page_param_taken";

    /// <summary>A page document against the components it shows: their drafts, or their live versions.</summary>
    public async Task<Error?> CheckPageAsync(Guid siteId, string document, bool live, CancellationToken cancellationToken)
    {
        var slots = DependencyExtractor.PageParamSlots(document);
        if (slots.Components.Count == 0)
        {
            return null;
        }

        var ids = slots.Components.Select(c => c.ComponentId).Distinct().ToList();
        var components = await ComponentNamesAsync(siteId, ids, live, cancellationToken).ConfigureAwait(false);

        // Who reads each name so far: null for the page itself, else a component.
        var owners = new Dictionary<string, Guid?>(StringComparer.Ordinal);
        foreach (var (name, _) in slots.Lists)
        {
            owners[name] = null;
        }

        foreach (var (componentId, pointer) in slots.Components)
        {
            if (!components.TryGetValue(componentId, out var component))
            {
                continue;
            }

            foreach (var name in component.Names)
            {
                if (!owners.TryGetValue(name, out var owner))
                {
                    owners[name] = componentId;
                    continue;
                }

                // A component shown twice brings the same lists twice: not a second reader.
                if (owner == componentId)
                {
                    continue;
                }

                return owner is { } other
                    ? PagesErrors.PageParamTaken(name, pointer, components[other].Label)
                    : PagesErrors.PageParamTaken(name, slots.Lists.First(l => l.Name == name).Pointer, component.Label);
            }
        }

        return null;
    }

    /// <summary>A component document against every page that shows it: their drafts, or their live versions.</summary>
    public async Task<Error?> CheckComponentAsync(Guid siteId, Guid componentId, string document, bool live, CancellationToken cancellationToken)
    {
        var own = DependencyExtractor.PageParamSlots(document).Lists;
        if (own.Count == 0)
        {
            return null;
        }

        var pages = await PagesShowingAsync(siteId, componentId, live, cancellationToken).ConfigureAwait(false);
        if (pages.Count == 0)
        {
            return null;
        }

        var others = pages.SelectMany(p => p.Components).Where(id => id != componentId).Distinct().ToList();
        var components = await ComponentNamesAsync(siteId, others, live, cancellationToken).ConfigureAwait(false);

        foreach (var page in pages)
        {
            foreach (var (name, pointer) in own)
            {
                if (page.Names.Contains(name))
                {
                    return PagesErrors.PageParamTaken(name, pointer, page.Title);
                }

                var other = page.Components
                    .Where(id => id != componentId && components.TryGetValue(id, out var c) && c.Names.Contains(name))
                    .Select(id => components[id].Label)
                    .FirstOrDefault();
                if (other is not null)
                {
                    return PagesErrors.PageParamTaken(name, pointer, other);
                }
            }
        }

        return null;
    }

    private sealed record ComponentParams(string Label, IReadOnlySet<string> Names);

    private sealed record ShowingPage(string Title, IReadOnlySet<string> Names, IReadOnlyList<Guid> Components);

    /// <summary>The page parameters of each component's draft or live version, with its name.</summary>
    private async Task<Dictionary<Guid, ComponentParams>> ComponentNamesAsync(
        Guid siteId, List<Guid> ids, bool live, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var labels = await database.ForSite<Component>(siteId)
            .AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken)
            .ConfigureAwait(false);

        IEnumerable<(Guid Id, IReadOnlySet<string> Names)> names;
        if (live)
        {
            // The publisher records each live version's page parameters (TB-11).
            names = (await database.ForSite<Publication>(siteId)
                    .AsNoTracking()
                    .Where(p => p.SubjectKind == SubjectKinds.Component && ids.Contains(p.SubjectId) && p.LiveVersionId != null)
                    .Select(p => new { p.SubjectId, p.PageParamNames })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false))
                .Select(p => (p.SubjectId, (IReadOnlySet<string>)p.PageParamNames.ToHashSet(StringComparer.Ordinal)));
        }
        else
        {
            names = (await database.ForSite<ComponentDraft>(siteId)
                    .AsNoTracking()
                    .Where(d => ids.Contains(d.ComponentId))
                    .Select(d => new { d.ComponentId, d.Document })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false))
                .Select(d => (d.ComponentId, DependencyExtractor.PageParamNames(d.Document)));
        }

        return names
            .Where(n => n.Names.Count > 0 && labels.ContainsKey(n.Id))
            .ToDictionary(n => n.Id, n => new ComponentParams(labels[n.Id], n.Names));
    }

    /// <summary>
    /// The pages that show the component, by address: their own page parameters and the
    /// components they show, from their drafts or their live versions.
    /// </summary>
    private async Task<List<ShowingPage>> PagesShowingAsync(Guid siteId, Guid componentId, bool live, CancellationToken cancellationToken)
    {
        if (live)
        {
            var pageIds = database.ForSite<LiveDependency>(siteId)
                .Where(d => d.SubjectKind == SubjectKinds.Page && d.TargetKind == DependencyKinds.Component && d.TargetId == componentId)
                .Select(d => d.SubjectId);

            var pages = await (
                    from page in database.ForSite<Page>(siteId)
                    where pageIds.Contains(page.Id)
                    join publication in database.ForSite<Publication>(siteId)
                        on new { Kind = SubjectKinds.Page, Id = page.Id } equals new { Kind = publication.SubjectKind, Id = publication.SubjectId }
                    where publication.LiveVersionId != null
                    orderby page.Path
                    select new { page.Id, page.Title, publication.PageParamNames })
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var ids = pages.Select(p => p.Id).ToList();
            var shown = await database.ForSite<LiveDependency>(siteId)
                .Where(d => d.SubjectKind == SubjectKinds.Page && ids.Contains(d.SubjectId) && d.TargetKind == DependencyKinds.Component)
                .Select(d => new { d.SubjectId, d.TargetId })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return [.. pages.Select(p => new ShowingPage(
                p.Title,
                p.PageParamNames.ToHashSet(StringComparer.Ordinal),
                [.. shown.Where(s => s.SubjectId == p.Id).Select(s => s.TargetId)]))];
        }

        var usingPages = database.ForSite<ComponentUsage>(siteId)
            .Where(u => u.ComponentId == componentId)
            .Select(u => u.PageId);

        var drafts = await (
                from page in database.ForSite<Page>(siteId)
                where usingPages.Contains(page.Id)
                join draft in database.ForSite<PageDraft>(siteId) on page.Id equals draft.PageId
                orderby page.Path
                select new { page.Id, page.Title, draft.Document })
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var draftIds = drafts.Select(d => d.Id).ToList();
        var usages = await database.ForSite<ComponentUsage>(siteId)
            .Where(u => draftIds.Contains(u.PageId))
            .Select(u => new { u.PageId, u.ComponentId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. drafts.Select(d => new ShowingPage(
            d.Title,
            DependencyExtractor.PageParamNames(d.Document),
            [.. usages.Where(u => u.PageId == d.Id).Select(u => u.ComponentId)]))];
    }
}
