using System.Text.RegularExpressions;

namespace QuickBuilder.Pages.Contracts;

/// <summary>
/// The rules every page address follows (010 FR-004 to FR-007, research P-03 and P-04).
/// </summary>
/// <remarks>
/// Part of the page schema contract (docs/contracts/page-schema.md §4 and §5):
/// <c>PageSchemaContractTests</c> fails the build when the values here and the document
/// disagree. The limits are contract constants, not operator settings, so a site that is
/// valid on one installation is valid on every installation.
/// </remarks>
public static partial class PageAddresses
{
    /// <summary>The longest slug, in characters.</summary>
    public const int MaxSlugLength = 100;

    /// <summary>The longest address, in characters, including every <c>/</c>.</summary>
    public const int MaxPathLength = 1024;

    /// <summary>How deep pages nest. A top-level page is at depth 1.</summary>
    public const int MaxDepth = 10;

    /// <summary>The most pages one site may hold.</summary>
    public const int MaxPagesPerSite = 2000;

    /// <summary>
    /// First address segments the platform answers before any page could: the
    /// administrative surface's prefixes (research P-04).
    /// </summary>
    /// <remarks>
    /// Platform prefixes the slug grammar cannot produce, such as <c>/_media</c>, need no
    /// entry; nor do <c>/robots.txt</c>, <c>/sitemap.xml</c> and <c>/sitemap-{n}.xml</c> (017),
    /// since a slug has no <c>.</c>. <c>ReservedAddressCoverageTests</c> fails the build when the platform gains a
    /// prefix that is neither listed here nor impossible to produce.
    /// </remarks>
    public static IReadOnlyList<string> ReservedFirstSegments { get; } = ["api", "admin", "health"];

    /// <summary>
    /// Whether <paramref name="slug"/> is lowercase letters, digits and single hyphens,
    /// 1 to <see cref="MaxSlugLength"/> characters, neither starting nor ending with a hyphen.
    /// </summary>
    public static bool IsValidSlug(string? slug) =>
        slug is { Length: > 0 and <= MaxSlugLength } && SlugPattern().IsMatch(slug);

    /// <summary>Whether a slug is reserved as the first segment of an address.</summary>
    public static bool IsReservedFirstSegment(string slug) =>
        ReservedFirstSegments.Contains(slug, StringComparer.Ordinal);

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();
}
