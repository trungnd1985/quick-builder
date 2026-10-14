namespace QuickBuilder.Pages.Contracts.Publishing;

/// <summary>
/// What the site's sitemap lists from Pages (017 research SE-14, SE-15, module-surface.md §2).
/// </summary>
/// <remarks>
/// Implemented over the read-only connection. Only live pages and live templates are visible.
/// The generation is read first, so content read after it is at least as new as the key
/// (012 research PR-05).
/// </remarks>
public interface ISitemapReader
{
    /// <summary>The site's sitemap generation; 0 when the site has never had one.</summary>
    Task<long> GetGenerationAsync(Guid siteId, CancellationToken cancellationToken = default);

    /// <summary>Every live page and every live template of the site, in path order.</summary>
    Task<SitemapPages> GetPagesAsync(Guid siteId, CancellationToken cancellationToken = default);
}

/// <summary>The live pages and templates of a site.</summary>
public sealed record SitemapPages(IReadOnlyList<SitemapPage> Pages, IReadOnlyList<SitemapTemplate> Templates);

/// <summary>A live page.</summary>
/// <param name="Path">Its normalised address.</param>
/// <param name="LiveSinceUtc">When its live version went live.</param>
/// <param name="Indexable">
/// Whether its live document lets it be listed: not hidden, not left out, no canonical override
/// (017 data-model §3.1). The site's own settings are the caller's to apply.
/// </param>
public sealed record SitemapPage(Guid PageId, string Path, DateTimeOffset LiveSinceUtc, bool Indexable);

/// <summary>A live template.</summary>
/// <param name="Pattern">Its address pattern, e.g. <c>/blog/{slug}</c>.</param>
/// <param name="CollectionId">The collection whose items it shows.</param>
/// <param name="LiveSinceUtc">When its live version went live.</param>
/// <param name="Indexable">As for <see cref="SitemapPage.Indexable"/>, for every item page.</param>
/// <param name="HideFieldId">
/// The switch field its "Hide from search engines" is bound to, if any (017 FR-007a): an item
/// whose published value is true is left out.
/// </param>
public sealed record SitemapTemplate(
    Guid PageId, string Pattern, Guid CollectionId, DateTimeOffset LiveSinceUtc, bool Indexable, string? HideFieldId);
