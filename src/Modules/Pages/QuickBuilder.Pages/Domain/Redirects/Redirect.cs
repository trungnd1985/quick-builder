using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Pages.Domain.Redirects;

/// <summary>
/// An old address of the site that sends visitors elsewhere (017 FR-015, research SE-09,
/// data-model §1.1).
/// </summary>
/// <remarks>
/// The source is stored normalised (<c>RedirectSource</c>), so lookups are exact. The
/// target is typed: a page or an item is followed to wherever it lives now (FR-018), a path or
/// an absolute address is used as written. No query means an empty <see cref="SourceQuery"/>,
/// so one plain unique index holds one redirect per source (FR-017).
/// </remarks>
internal sealed partial class Redirect : ISiteOwned
{
    // EF Core materialisation.
    private Redirect()
    {
        MatchKind = RedirectMatchKinds.Exact;
        SourcePath = string.Empty;
        SourceQuery = string.Empty;
        TargetKind = RedirectTargetKinds.Path;
        Origin = RedirectOrigins.Manual;
    }

    public Guid Id { get; private set; }

    public Guid SiteId { get; private set; }

    /// <summary>One of <see cref="RedirectMatchKinds"/>.</summary>
    public string MatchKind { get; private set; }

    /// <summary>The normalised path: lower-case, no trailing slash (<c>RedirectSource</c>).</summary>
    public string SourcePath { get; private set; }

    /// <summary>The canonical query (<c>CanonicalQuery</c>); empty when the source has none.</summary>
    public string SourceQuery { get; private set; }

    /// <summary><c>301</c> or <c>302</c>.</summary>
    public int StatusCode { get; private set; }

    /// <summary>One of <see cref="RedirectTargetKinds"/>.</summary>
    public string TargetKind { get; private set; }

    public Guid? TargetPageId { get; private set; }

    public Guid? TargetCollectionId { get; private set; }

    public Guid? TargetItemId { get; private set; }

    /// <summary>A site path, with an optional query, when <see cref="TargetKind"/> is <c>path</c>.</summary>
    public string? TargetPath { get; private set; }

    /// <summary>An absolute <c>http</c> or <c>https</c> address when <see cref="TargetKind"/> is <c>url</c>.</summary>
    public string? TargetUrl { get; private set; }

    public bool Enabled { get; private set; }

    public string? Note { get; private set; }

    /// <summary>One of <see cref="RedirectOrigins"/>.</summary>
    public string Origin { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    /// <summary>PostgreSQL's <c>xmin</c>: the concurrency token the API calls <c>version</c>.</summary>
    public uint Version { get; private set; }
}

/// <summary>How a redirect's source matches a request (FR-033).</summary>
internal static class RedirectMatchKinds
{
    public const string Exact = "exact";

    /// <summary>The source and every address below it, never the source itself.</summary>
    public const string Prefix = "prefix";

    public static IReadOnlyList<string> All { get; } = [Exact, Prefix];
}

/// <summary>What a redirect sends visitors to (research SE-09).</summary>
internal static class RedirectTargetKinds
{
    public const string Page = "page";

    public const string Item = "item";

    public const string Path = "path";

    public const string Url = "url";

    public static IReadOnlyList<string> All { get; } = [Page, Item, Path, Url];
}

/// <summary>How a redirect came to exist.</summary>
internal static class RedirectOrigins
{
    public const string Manual = "manual";

    public const string Import = "import";

    /// <summary>Accepted from the offer of an address change (FR-028).</summary>
    public const string Offer = "offer";

    public static IReadOnlyList<string> All { get; } = [Manual, Import, Offer];
}
