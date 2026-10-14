using QuickBuilder.Cms.Contracts.Publishing;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Pages.Contracts.Redirects;
using QuickBuilder.Rendering.Contracts;
using QuickBuilder.Rendering.Rendering;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Rendering.Features.RenderPage;

/// <summary>
/// Answers a request with a redirect (017 FR-016 to FR-018, FR-021a; research SE-10;
/// public-seo.md §2).
/// </summary>
/// <remarks>
/// A page or item target is followed to where it lives now. A prefix redirect keeps the rest
/// of the address. The request's query goes along when neither the source nor the target has
/// one. A 301 may be cached for five minutes, so a member who changes their mind is not stuck
/// with a browser's permanent memory of it; a 302 is not cached. Each answer is counted in
/// memory only (<see cref="IRedirectHits"/>).
/// </remarks>
internal sealed class RedirectResponder(IPublishedContentReader pages, IPublishedItemReader items, IRedirectHits hits)
{
    /// <summary>The redirect response, or null when its page or item target is not live (FR-018).</summary>
    public async Task<PublicResponse?> RespondAsync(
        SiteDto site, RedirectMatch match, string basePath, string? requestQuery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(match);

        var target = await TargetAsync(site.Id, match, cancellationToken).ConfigureAwait(false);
        if (target is null)
        {
            return null;
        }

        var location = target.IsAbsolute ? target.Address : PageShell.TrimBase(basePath) + target.Address;
        if (!match.FromQuerySource && !location.Contains('?', StringComparison.Ordinal) && !string.IsNullOrEmpty(requestQuery) && requestQuery != "?")
        {
            location += requestQuery[0] == '?' ? requestQuery : "?" + requestQuery;
        }

        hits.Record(site.Id, match.RedirectId);
        return Redirect(match.StatusCode, AsciiOnly(location));
    }

    /// <summary>A redirect response to <paramref name="location"/>, as public-seo.md §2 writes it.</summary>
    public static PublicResponse Redirect(int statusCode, string location) => new(
        statusCode,
        "text/plain; charset=utf-8",
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Location"] = location,
            ["Cache-Control"] = statusCode == StatusCodes.Status301MovedPermanently ? "public, max-age=300" : "no-cache",
        },
        ReadOnlyMemory<byte>.Empty);

    private async Task<Target?> TargetAsync(Guid siteId, RedirectMatch match, CancellationToken cancellationToken)
    {
        switch (match.TargetKind)
        {
            case RedirectTargetKind.Page when match.PageId is { } pageId:
                var targets = await pages.GetLinkTargetsAsync(siteId, [pageId], cancellationToken).ConfigureAwait(false);
                return targets.TryGetValue(pageId, out var page) && page.IsLive ? new Target(Join(page.Path, match.Remainder), false) : null;

            case RedirectTargetKind.Item when match.CollectionId is { } collectionId && match.ItemId is { } itemId:
                var patterns = await pages.GetLiveTemplatePatternsAsync(siteId, [collectionId], cancellationToken).ConfigureAwait(false);
                if (!patterns.TryGetValue(collectionId, out var pattern))
                {
                    return null;
                }

                var found = await items.GetAsync(siteId, [itemId], cancellationToken).ConfigureAwait(false);
                return found.TryGetValue(itemId, out var item)
                    ? new Target(pattern.Replace("{slug}", Uri.EscapeDataString(item.Slug), StringComparison.Ordinal), false)
                    : null;

            case RedirectTargetKind.Path when match.Path is { } path:
                return new Target(Join(path, match.Remainder), false);

            case RedirectTargetKind.Url when match.Url is { } url:
                return new Target(url, true);

            default:
                return null;
        }
    }

    /// <summary>A target address followed by the rest of a prefix match.</summary>
    private static string Join(string address, string remainder)
    {
        if (remainder.Length == 0)
        {
            return address;
        }

        var query = address.IndexOf('?', StringComparison.Ordinal);
        return query < 0
            ? address.TrimEnd('/') + remainder
            : address[..query].TrimEnd('/') + remainder + address[query..];
    }

    /// <summary>
    /// <paramref name="location"/> with every character outside printable ASCII percent-encoded
    /// as UTF-8: a header value is ASCII, and paths such as <c>/café</c> are not.
    /// </summary>
    internal static string AsciiOnly(string location)
    {
        if (location.All(c => c is > (char)0x20 and < (char)0x7F))
        {
            return location;
        }

        var builder = new System.Text.StringBuilder(location.Length + 16);
        Span<byte> bytes = stackalloc byte[4];
        foreach (var rune in location.EnumerateRunes())
        {
            if (rune.Value is > 0x20 and < 0x7F)
            {
                builder.Append((char)rune.Value);
                continue;
            }

            var length = rune.EncodeToUtf8(bytes);
            foreach (var b in bytes[..length])
            {
                builder.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }

    private sealed record Target(string Address, bool IsAbsolute);
}
