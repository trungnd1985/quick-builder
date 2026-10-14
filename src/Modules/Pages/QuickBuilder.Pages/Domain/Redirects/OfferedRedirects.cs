namespace QuickBuilder.Pages.Domain.Redirects;

/// <summary>A page's place in the tree, as the redirect offers compare it before and after a change.</summary>
/// <param name="Path">Its address, or for a template its pattern (<c>/blog/{slug}</c>).</param>
internal sealed record TreeAddress(string Path, Guid? ParentId, bool IsTemplate);

/// <summary>One redirect offered for a change (017 data-model §3.4).</summary>
internal sealed record OfferedRedirect(string Match, string SourcePath, RedirectTarget Target);

/// <summary>
/// The redirects a structural change offers (017 FR-028, FR-030; research SE-11): the
/// difference between the tree before and after it, for the pages that were live.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A live page whose address changed: its old address, exactly, to the page.</item>
/// <item>A live template whose pattern changed: the old pattern's prefix to the new one's, so
/// every item address follows with one redirect (clarification Q1).</item>
/// <item>A live page made a template: its own old address to its parent.</item>
/// <item>A template that only changed collection offers nothing: its addresses stay.</item>
/// <item>A template turned back into a page offers nothing for its items: they have no new
/// address, and a prefix redirect keeps the rest of the path (public-seo.md §2), so one from
/// the old prefix to the parent would send <c>/blog/x</c> back to <c>/blog/x</c>.</item>
/// </list>
/// Pure: what is skipped because live content now holds it, and what is retargeted or turned
/// off, is decided against the site's redirects when the offers are applied.
/// </remarks>
internal static class OfferedRedirects
{
    public static IReadOnlyList<OfferedRedirect> Plan(
        IReadOnlyDictionary<Guid, TreeAddress> before,
        IReadOnlyDictionary<Guid, TreeAddress> after,
        IReadOnlySet<Guid> live)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(live);

        var offers = new List<OfferedRedirect>();
        foreach (var (pageId, old) in before.OrderBy(p => p.Value.Path, StringComparer.Ordinal))
        {
            if (!live.Contains(pageId) || !after.TryGetValue(pageId, out var now))
            {
                continue;
            }

            switch (old.IsTemplate, now.IsTemplate)
            {
                case (false, false) when old.Path != now.Path:
                    offers.Add(new OfferedRedirect(RedirectMatchKinds.Exact, old.Path, RedirectTarget.ToPage(pageId)));
                    break;

                case (true, true) when PrefixOf(old.Path) is { } from && PrefixOf(now.Path) is { } to && from != to:
                    offers.Add(new OfferedRedirect(RedirectMatchKinds.Prefix, from, RedirectTarget.ToPath(to)));
                    break;

                case (false, true) when now.ParentId is { } parent:
                    offers.Add(new OfferedRedirect(RedirectMatchKinds.Exact, old.Path, RedirectTarget.ToPage(parent)));
                    break;
            }
        }

        return offers;
    }

    /// <summary>
    /// The address every item of a template pattern starts with: the pattern without its last
    /// segment (<c>/blog/{slug}</c> → <c>/blog</c>); null for a pattern directly under the root.
    /// </summary>
    public static string? PrefixOf(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        var end = pattern.LastIndexOf('/');
        return end > 0 ? pattern[..end] : null;
    }
}
