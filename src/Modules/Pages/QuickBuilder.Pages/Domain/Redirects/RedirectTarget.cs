using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Domain.Redirects;

/// <summary>
/// Where a redirect sends visitors (017 research SE-09): a page or an item, followed to its
/// current address, a path on the site, or an absolute address.
/// </summary>
internal sealed record RedirectTarget(string Kind, Guid? PageId, Guid? CollectionId, Guid? ItemId, string? Path, string? Url)
{
    public static RedirectTarget ToPage(Guid pageId) => new(RedirectTargetKinds.Page, pageId, null, null, null, null);

    public static RedirectTarget ToItem(Guid collectionId, Guid itemId) => new(RedirectTargetKinds.Item, null, collectionId, itemId, null, null);

    public static RedirectTarget ToPath(string path) => new(RedirectTargetKinds.Path, null, null, null, path, null);

    public static RedirectTarget ToUrl(string url) => new(RedirectTargetKinds.Url, null, null, null, null, url);
}

/// <summary>The rules a target follows before it is stored (017 FR-017, FR-033).</summary>
internal static class RedirectTargets
{
    public const int MaxUrlLength = 2048;

    public const int MaxPathLength = 1600;

    /// <summary>An absolute <c>http</c> or <c>https</c> address, without user info or spaces.</summary>
    public static Result<RedirectTarget> Url(string? url)
    {
        var text = url?.Trim() ?? string.Empty;
        if (text.Length is 0 or > MaxUrlLength
            || text.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))
            || !Uri.TryCreate(text, UriKind.Absolute, out var parsed)
            || parsed.Scheme is not ("http" or "https")
            || parsed.UserInfo.Length > 0
            || string.IsNullOrEmpty(parsed.Host))
        {
            return Invalid("use an http or https address, such as https://example.com/page");
        }

        return Result.Success(RedirectTarget.ToUrl(text));
    }

    /// <summary>A path on the site, with an optional query: starts with one <c>/</c>.</summary>
    public static Result<RedirectTarget> SitePath(string? path)
    {
        var text = path?.Trim() ?? string.Empty;
        if (text.Length is 0 or > MaxPathLength
            || text[0] != '/'
            || text.StartsWith("//", StringComparison.Ordinal)
            || text.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c == '\\'))
        {
            return Invalid("use a path on this site, such as /new-page");
        }

        return Result.Success(RedirectTarget.ToPath(text));
    }

    /// <summary>FR-033: a prefix sends to a page or a path, never to an item or another site.</summary>
    public static Result CheckForMatch(RedirectTarget target, string matchKind)
    {
        ArgumentNullException.ThrowIfNull(target);

        return matchKind == RedirectMatchKinds.Prefix && target.Kind is not (RedirectTargetKinds.Page or RedirectTargetKinds.Path)
            ? Result.Failure(PagesErrors.RedirectTargetInvalid("a prefix redirect sends to a page or a path on this site"))
            : Result.Success();
    }

    private static Result<RedirectTarget> Invalid(string reason) =>
        Result.Failure<RedirectTarget>(PagesErrors.RedirectTargetInvalid(reason));
}
