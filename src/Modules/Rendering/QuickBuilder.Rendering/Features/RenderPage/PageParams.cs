using System.Globalization;

namespace QuickBuilder.Rendering.Features.RenderPage;

/// <summary>
/// The page numbers a request asks of a page's paginated lists, normalised for the cache key
/// (016 research TB-11, FR-027).
/// </summary>
/// <remarks>
/// Only the parameters the page's own lists declare are kept, and only whole numbers from 2 to
/// <see cref="MaxPage"/>: <c>1</c>, a malformed value and every other parameter mean the first
/// page and are dropped. So <c>/blog</c>, <c>/blog?page=1</c> and <c>/blog?utm=x</c> share one key,
/// and a visitor cannot create more cache entries than a page has real pages, up to the cap.
/// </remarks>
internal sealed class PageParams
{
    /// <summary>The highest page number a list is asked for; anything above is the first page.</summary>
    public const int MaxPage = 10_000;

    private PageParams(SortedDictionary<string, int> pages) => Pages = pages;

    public static PageParams None { get; } = new(new SortedDictionary<string, int>(StringComparer.Ordinal));

    /// <summary>The kept page numbers by parameter, in ordinal order.</summary>
    public IReadOnlyDictionary<string, int> Pages { get; }

    /// <summary>The kept pairs as text, for the cache key: <c>page=3&amp;people=2</c>, or empty.</summary>
    public string Key => string.Join('&', Pages.Select(p => $"{p.Key}={p.Value.ToString(CultureInfo.InvariantCulture)}"));

    /// <summary>
    /// The page numbers in <paramref name="query"/> for the <paramref name="declared"/> parameters
    /// of the page's paginated lists. A parameter given more than once is read from its first value.
    /// </summary>
    public static PageParams From(IReadOnlyCollection<string> declared, IReadOnlyDictionary<string, string?>? query)
    {
        ArgumentNullException.ThrowIfNull(declared);

        if (declared.Count == 0 || query is null || query.Count == 0)
        {
            return None;
        }

        var pages = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var name in declared)
        {
            if (query.TryGetValue(name, out var raw) && Number(raw) is { } page)
            {
                pages[name] = page;
            }
        }

        return pages.Count == 0 ? None : new PageParams(pages);
    }

    /// <summary>The page a list with <paramref name="param"/> shows: 1 unless the request asked for another.</summary>
    public int PageOf(string param) => Pages.TryGetValue(param, out var page) ? page : 1;

    private static int? Number(string? raw) =>
        raw is { Length: > 0 and <= 5 }
        && raw.All(char.IsAsciiDigit)
        && raw[0] != '0'
        && int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var page)
        && page is >= 2 and <= MaxPage
            ? page
            : null;
}
