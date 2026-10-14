using System.Text.RegularExpressions;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Pages.Contracts.Redirects;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Domain.Redirects;

/// <summary>
/// A redirect's source as stored: a normalised path and a canonical query (017 FR-017,
/// FR-017a, research SE-09).
/// </summary>
/// <remarks>
/// The path is normalised as the public path normalises a request (lower-case, no trailing
/// slash, percent-decoded once), so a stored source and a requested address compare exactly.
/// Sources the platform answers before any redirect could are refused: the administrative
/// prefixes, the media and asset paths, robots.txt and the sitemaps.
/// </remarks>
internal static partial class RedirectSource
{
    /// <summary>The path and canonical query of <paramref name="input"/>, or why it cannot be a source.</summary>
    public static Result<(string Path, string Query)> Parse(string? input, string matchKind)
    {
        var text = input?.Trim() ?? string.Empty;
        var questionMark = text.IndexOf('?', StringComparison.Ordinal);
        var rawPath = questionMark < 0 ? text : text[..questionMark];
        var rawQuery = questionMark < 0 ? string.Empty : text[(questionMark + 1)..];

        if (NormalisePath(Decode(rawPath)) is not { } path)
        {
            return Invalid("use a path on this site, such as /old-page");
        }

        if (path.Length > PageAddresses.MaxPathLength)
        {
            return Invalid($"it is longer than {PageAddresses.MaxPathLength} characters");
        }

        if (IsPlatformPath(path))
        {
            return Invalid("the platform answers that address itself");
        }

        var query = CanonicalQuery.FromRaw(rawQuery);
        if (query.Length > CanonicalQuery.MaxLength)
        {
            return Invalid($"its query is longer than {CanonicalQuery.MaxLength} characters");
        }

        if (query.Length > 0 && matchKind != RedirectMatchKinds.Exact)
        {
            return Invalid("only an exact redirect can match a query");
        }

        if (path == "/" && (matchKind == RedirectMatchKinds.Prefix || query.Length == 0))
        {
            return Invalid("the home page itself cannot be redirected");
        }

        return Result.Success((path, query));
    }

    /// <summary>
    /// A path in the form the public path looks addresses up in: lower-case, one trailing slash
    /// dropped. Null when no address can have it: not starting with one <c>/</c>, empty or dot
    /// segments, backslashes, control characters (012 PathNormaliser).
    /// </summary>
    public static string? NormalisePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path[0] != '/' || path.StartsWith("//", StringComparison.Ordinal))
        {
            return null;
        }

        if (path == "/")
        {
            return "/";
        }

        var trimmed = path[^1] == '/' ? path[..^1] : path;
        // As PathNormaliser: a literal % after decoding can never be requested, so never matches.
        if (trimmed.Any(c => char.IsControl(c) || c is '\\' or '%'))
        {
            return null;
        }

        foreach (var segment in trimmed[1..].Split('/'))
        {
            if (segment.Length == 0 || segment is "." or "..")
            {
                return null;
            }
        }

        return trimmed.ToLowerInvariant();
    }

    /// <summary>Whether the platform answers <paramref name="path"/> before any redirect.</summary>
    public static bool IsPlatformPath(string path)
    {
        var first = path.TrimStart('/').Split('/', 2)[0];
        return PageAddresses.IsReservedFirstSegment(first)
            || first is "_media" or "_assets" or "robots.txt" or "sitemap.xml"
            || SitemapFile().IsMatch(first);
    }

    private static string Decode(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch (UriFormatException)
        {
            return value;
        }
    }

    private static Result<(string, string)> Invalid(string reason) =>
        Result.Failure<(string, string)>(PagesErrors.RedirectSourceInvalid(reason));

    [GeneratedRegex("^sitemap-[0-9]+\\.xml$", RegexOptions.CultureInvariant)]
    private static partial Regex SitemapFile();
}
