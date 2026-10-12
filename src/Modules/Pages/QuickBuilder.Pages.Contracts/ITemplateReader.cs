namespace QuickBuilder.Pages.Contracts;

/// <summary>
/// Which page is a collection's template, at which address pattern, and whether it is live
/// (016 research TB-12, module-surface.md §3).
/// </summary>
/// <remarks>
/// For the admin side: Cms tells an item where its page is, or why it has none (FR-007). The
/// public path reads templates through <see cref="Publishing.IPublishedContentReader"/>.
/// </remarks>
public interface ITemplateReader
{
    /// <summary>The template of <paramref name="collectionId"/>, or null when the collection has none.</summary>
    Task<TemplateInfo?> GetTemplateAsync(Guid siteId, Guid collectionId, CancellationToken cancellationToken = default);

    /// <summary>The templates of the collections among <paramref name="collectionIds"/> that have one.</summary>
    Task<IReadOnlyDictionary<Guid, TemplateInfo>> GetTemplatesAsync(
        Guid siteId, IReadOnlyCollection<Guid> collectionIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether a live ordinary page has the address <paramref name="path"/>, so an item address
    /// there is hidden by it (FR-005, FR-007).
    /// </summary>
    Task<bool> LivePageAtAsync(Guid siteId, string path, CancellationToken cancellationToken = default);
}

/// <summary>A collection's template page.</summary>
/// <param name="Pattern">The address pattern, such as <c>/blog/{slug}</c> (TB-02).</param>
/// <param name="IsLive">Whether the template is published, so its items have addresses.</param>
public sealed record TemplateInfo(Guid PageId, string Pattern, bool IsLive)
{
    /// <summary>The placeholder in <see cref="Pattern"/> that an item's slug replaces.</summary>
    public const string SlugPlaceholder = "{slug}";

    /// <summary>The address of the item whose published slug is <paramref name="slug"/>.</summary>
    public string AddressOf(string slug) => Pattern.Replace(SlugPlaceholder, slug, StringComparison.Ordinal);
}
