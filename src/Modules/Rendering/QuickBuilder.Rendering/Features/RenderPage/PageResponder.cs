using System.Text;
using Microsoft.Extensions.Logging;
using QuickBuilder.Editor.Contracts;
using QuickBuilder.Media.Contracts;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Rendering.Contracts;
using QuickBuilder.Rendering.Domain;
using QuickBuilder.Rendering.Rendering;
using QuickBuilder.Shared.Infrastructure.Caching;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Rendering.Features.RenderPage;

/// <summary>
/// Answers a visitor's request for a page: the live page as complete HTML, or the site's
/// not-found page (012 FR-022, research PR-05, PR-09, PR-12; public-rendering.md §3).
/// </summary>
/// <remarks>
/// Everything is read through read-only readers of other modules (PR-06). The page's key —
/// its generation, the design number — is read first and the content after, so anything
/// published in between gets a newer key (PR-05).
/// </remarks>
internal sealed class PageResponder(
    IPublishedContentReader pages,
    IPublishedDesignReader designs,
    IPublicMediaReader media,
    PageRenderer renderer,
    IPublicNotFoundPage notFound,
    ICacheProvider cache,
    RenderMetrics metrics,
    ILogger<PageResponder> logger)
{
    /// <summary>A page stays cached this long after it was last served (research PR-07).</summary>
    private static readonly TimeSpan Sliding = TimeSpan.FromHours(1);

    /// <summary>
    /// The response for <paramref name="path"/>, already normalised: <c>304</c> when the
    /// visitor's copy is current, else the page from the cache or freshly rendered.
    /// </summary>
    public async Task<PublicResponse> RespondAsync(
        SiteDto site, string path, string basePath, string? ifNoneMatch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(site);

        // The key first, the content after (PR-05).
        var live = await pages.FindLiveByPathAsync(site.Id, path, cancellationToken).ConfigureAwait(false);
        if (live is null)
        {
            return await notFound.RenderAsync(site, cancellationToken).ConfigureAwait(false);
        }

        var designNumber = await designs.GetLiveNumberAsync(site.Id, cancellationToken).ConfigureAwait(false);
        var key = new RenderKey(site.Id, live.PageId, live.Generation, designNumber, site.Version, basePath);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ETag"] = key.ETag,
            // Stored, but checked with the server every time: a publish shows at once.
            ["Cache-Control"] = "public, no-cache",
        };

        if (key.IsMatchedBy(ifNoneMatch))
        {
            return new PublicResponse(StatusCodes.Status304NotModified, "text/html; charset=utf-8", headers, ReadOnlyMemory<byte>.Empty);
        }

        var rendered = await CachedAsync(key, site, live.PageId, basePath, cancellationToken).ConfigureAwait(false);
        return rendered is null
            ? await notFound.RenderAsync(site, cancellationToken).ConfigureAwait(false)
            : ToResponse(rendered, headers);
    }

    /// <summary>
    /// The page under its key, rendered once however many ask at once. A cache that fails is
    /// a slower page, never a wrong or missing one (FR-034).
    /// </summary>
    private async Task<RenderedPage?> CachedAsync(RenderKey key, SiteDto site, Guid pageId, string basePath, CancellationToken cancellationToken)
    {
        try
        {
            return await cache.GetOrCreateAsync(
                    key.CacheKey,
                    async ct => await RenderAsync(site, pageId, basePath, ct).ConfigureAwait(false) ?? throw new PageGoneException(),
                    page => new CacheEntrySettings(page.Html.Length, Sliding),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (PageGoneException)
        {
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The page cache failed; page {PageId} of site {SiteId} was rendered without it.", pageId, site.Id);
            return await RenderAsync(site, pageId, basePath, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The page stopped being live between finding it and reading it: nothing to cache.</summary>
    private sealed class PageGoneException : Exception;

    /// <summary>Renders a live page; null when it stopped being live since it was found.</summary>
    public async Task<RenderedPage?> RenderAsync(SiteDto site, Guid pageId, string basePath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(site);

        var version = await pages.GetLivePageAsync(site.Id, pageId, cancellationToken).ConfigureAwait(false);
        if (version is null)
        {
            return null;
        }

        var design = await designs.GetLiveAsync(site.Id, cancellationToken).ConfigureAwait(false);

        var references = new RenderReferences();
        references.Collect(version.Document);

        var components = await pages.GetLiveComponentsAsync(site.Id, references.Components, cancellationToken).ConfigureAwait(false);
        foreach (var component in components.Values)
        {
            references.Collect(component.Document);
        }

        var targets = await pages.GetLinkTargetsAsync(site.Id, references.Pages, cancellationToken).ConfigureAwait(false);
        var files = await media.GetAsync(site.Id, references.Media, cancellationToken).ConfigureAwait(false);

        var inputs = new RenderInputs(version.Title, version.Document, design.Document, components, targets, files, site.DefaultLanguage, basePath);
        metrics.Rendered();
        return Render(inputs, renderer);
    }

    /// <summary>A page from its inputs: the body, the shell, and the style hash its policy allows.</summary>
    public static RenderedPage Render(RenderInputs inputs, PageRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(renderer);

        var context = new RenderContext(inputs, BlockDefaults.Current);
        var body = new HtmlWriter();
        renderer.RenderDocument(inputs.Document, PageRenderer.PageScope, context, body);

        var document = PageShell.Write(inputs.Title, inputs.Language, inputs.BasePath, context.Tokens, context.NodeRules, body.ToString());
        return new RenderedPage(Encoding.UTF8.GetBytes(document.Html), document.StyleHash);
    }

    /// <summary>The headers of public-rendering.md §3 around a rendered page.</summary>
    public static PublicResponse ToResponse(RenderedPage page, IReadOnlyDictionary<string, string>? extraHeaders = null)
    {
        ArgumentNullException.ThrowIfNull(page);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Content-Security-Policy"] = ContentSecurityPolicy(page.StyleHash),
            ["X-Content-Type-Options"] = "nosniff",
            ["Referrer-Policy"] = "strict-origin-when-cross-origin",
            ["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()",
        };

        foreach (var (name, value) in extraHeaders ?? new Dictionary<string, string>())
        {
            headers[name] = value;
        }

        return new PublicResponse(StatusCodes.Status200OK, "text/html; charset=utf-8", headers, page.Html);
    }

    /// <summary>
    /// The strictest policy the page allows: its own stylesheet and exactly its one style
    /// element, images from the site, and the two allow-listed video players (PR-12).
    /// </summary>
    public static string ContentSecurityPolicy(string styleHash) =>
        "default-src 'none'; " +
        $"style-src 'self' 'sha256-{styleHash}'; " +
        "img-src 'self'; " +
        "frame-src https://www.youtube-nocookie.com https://player.vimeo.com; " +
        "base-uri 'none'; " +
        "form-action 'none'; " +
        "frame-ancestors 'self'";
}
