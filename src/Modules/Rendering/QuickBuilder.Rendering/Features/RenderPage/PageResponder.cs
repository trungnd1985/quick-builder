using System.Text;
using Microsoft.Extensions.Logging;
using QuickBuilder.Cms.Contracts.Publishing;
using QuickBuilder.Editor.Contracts;
using QuickBuilder.Media.Contracts;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Pages.Contracts.Redirects;
using QuickBuilder.Rendering.Binding;
using QuickBuilder.Rendering.Contracts;
using QuickBuilder.Rendering.Domain;
using QuickBuilder.Rendering.Rendering;
using QuickBuilder.Rendering.Seo;
using QuickBuilder.Shared.Infrastructure.Caching;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Rendering.Features.RenderPage;

/// <summary>
/// Answers a visitor's request for a page: the live page as complete HTML, an item's page from
/// its collection's template, or the site's not-found page (012 FR-022, research PR-05, PR-09,
/// PR-12; 016 FR-005, research TB-03, TB-10; public-rendering.md §3).
/// </summary>
/// <remarks>
/// Everything is read through read-only readers of other modules (PR-06). The page's key —
/// its generation, the design number, for an item page the item's render stamp — is read
/// first and the content after, so anything published in between gets a newer key (PR-05).
/// <para>
/// Addresses are matched in the contract's order: a live static page first, then a live
/// template under the address's parent with a published item of that slug, then the not-found
/// page, the same bytes whatever the reason (016 FR-005, FR-006).
/// </para>
/// </remarks>
internal sealed class PageResponder(
    IPublishedContentReader pages,
    IPublishedDesignReader designs,
    IPublicMediaReader media,
    IPublishedItemReader items,
    BindingFetcher bindings,
    PageRenderer renderer,
    IPublicNotFoundPage notFound,
    IRedirectReader redirectReader,
    RedirectResponder redirects,
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
        SiteDto site,
        string path,
        string basePath,
        string? ifNoneMatch,
        IReadOnlyDictionary<string, string?>? query,
        string canonicalQuery,
        string? rawQuery,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(site);

        // 017 FR-016, FR-017a: a redirect with this exact query is more specific than the
        // page at its path, so it is matched first.
        if (canonicalQuery.Length > 0
            && await redirectReader.FindQueryAsync(site.Id, path, canonicalQuery, cancellationToken).ConfigureAwait(false) is { } queried)
        {
            return await redirects.RespondAsync(site, queried, basePath, rawQuery, cancellationToken).ConfigureAwait(false)
                ?? await notFound.RenderAsync(site, cancellationToken).ConfigureAwait(false);
        }

        // The key first, the content after (PR-05).
        var live = await pages.FindLiveByPathAsync(site.Id, path, cancellationToken).ConfigureAwait(false);
        if (live is null)
        {
            return await RespondItemAsync(site, path, basePath, ifNoneMatch, query, rawQuery, cancellationToken).ConfigureAwait(false);
        }

        // 016 TB-11: the page numbers its lists show are part of the key; other parameters are not.
        var request = new ListRequest(path, PageParams.From(live.Params, query));
        var designNumber = await designs.GetLiveNumberAsync(site.Id, cancellationToken).ConfigureAwait(false);
        var key = new RenderKey(site.Id, live.PageId, live.Generation, designNumber, site.Version, basePath, request.Params.Key);
        var headers = Headers(key.ETag);

        if (key.IsMatchedBy(ifNoneMatch))
        {
            return NotModified(headers);
        }

        var rendered = await CachedAsync(key.CacheKey, live.PageId, site, ct => RenderAsync(site, live.PageId, basePath, request, ct), cancellationToken)
            .ConfigureAwait(false);
        return rendered is null
            ? await notFound.RenderAsync(site, cancellationToken).ConfigureAwait(false)
            : ToResponse(rendered, headers);
    }

    /// <summary>An item's page, when the address is a live template's with a published item's slug (016 TB-03).</summary>
    private async Task<PublicResponse> RespondItemAsync(
        SiteDto site,
        string path,
        string basePath,
        string? ifNoneMatch,
        IReadOnlyDictionary<string, string?>? query,
        string? rawQuery,
        CancellationToken cancellationToken)
    {
        if (TemplateMatcher.Split(path) is not { } split
            || await pages.FindLiveTemplateAsync(site.Id, split.ParentPath, cancellationToken).ConfigureAwait(false) is not { } template
            || await items.FindBySlugAsync(site.Id, template.CollectionId, split.Slug, cancellationToken).ConfigureAwait(false) is not { } item)
        {
            return await RedirectOrNotFoundAsync(site, path, basePath, rawQuery, cancellationToken).ConfigureAwait(false);
        }

        var request = new ListRequest(path, PageParams.From(template.Params, query));
        var designNumber = await designs.GetLiveNumberAsync(site.Id, cancellationToken).ConfigureAwait(false);
        var key = new ItemRenderKey(
            site.Id, template.PageId, template.Generation, item.ItemId, item.RenderStamp, designNumber, site.Version, basePath, request.Params.Key);
        var headers = Headers(key.ETag);

        if (key.IsMatchedBy(ifNoneMatch))
        {
            return NotModified(headers);
        }

        var rendered = await CachedAsync(
                key.CacheKey,
                template.PageId,
                site,
                ct => RenderItemAsync(site, template.PageId, item.ItemId, basePath, request, ct),
                cancellationToken)
            .ConfigureAwait(false);
        return rendered is null
            ? await notFound.RenderAsync(site, cancellationToken).ConfigureAwait(false)
            : ToResponse(rendered, headers);
    }

    /// <summary>
    /// What answers an address no content answers (017 FR-016): an exact redirect, else the
    /// longest prefix redirect, else the not-found page. A redirect to a page or item that is no
    /// longer live is the not-found page too (FR-018).
    /// </summary>
    private async Task<PublicResponse> RedirectOrNotFoundAsync(SiteDto site, string path, string basePath, string? rawQuery, CancellationToken cancellationToken)
    {
        if (await redirectReader.FindAsync(site.Id, path, cancellationToken).ConfigureAwait(false) is { } match
            && await redirects.RespondAsync(site, match, basePath, rawQuery, cancellationToken).ConfigureAwait(false) is { } redirect)
        {
            return redirect;
        }

        return await notFound.RenderAsync(site, cancellationToken).ConfigureAwait(false);
    }

    private static Dictionary<string, string> Headers(string etag) => new(StringComparer.OrdinalIgnoreCase)
    {
        ["ETag"] = etag,
        // Stored, but checked with the server every time: a publish shows at once.
        ["Cache-Control"] = "public, no-cache",
    };

    private static PublicResponse NotModified(Dictionary<string, string> headers) =>
        new(StatusCodes.Status304NotModified, "text/html; charset=utf-8", headers, ReadOnlyMemory<byte>.Empty);

    /// <summary>
    /// The page under its key, rendered once however many ask at once. A cache that fails is
    /// a slower page, never a wrong or missing one (FR-034).
    /// </summary>
    private async Task<RenderedPage?> CachedAsync(
        string cacheKey, Guid pageId, SiteDto site, Func<CancellationToken, Task<RenderedPage?>> render, CancellationToken cancellationToken)
    {
        try
        {
            return await cache.GetOrCreateAsync(
                    cacheKey,
                    async ct => await render(ct).ConfigureAwait(false) ?? throw new PageGoneException(),
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
            return await render(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The page stopped being live between finding it and reading it: nothing to cache.</summary>
    private sealed class PageGoneException : Exception;

    /// <summary>Renders a live page; null when it stopped being live since it was found.</summary>
    public async Task<RenderedPage?> RenderAsync(SiteDto site, Guid pageId, string basePath, ListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(site);

        var version = await pages.GetLivePageAsync(site.Id, pageId, cancellationToken).ConfigureAwait(false);
        return version is null
            ? null
            : await RenderVersionAsync(site, pageId, version, basePath, pageItem: null, request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Renders an item's page from its template's live version; null when the template or the
    /// item stopped being live since they were found (016 FR-030).
    /// </summary>
    public async Task<RenderedPage?> RenderItemAsync(
        SiteDto site, Guid templatePageId, Guid itemId, string basePath, ListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(site);

        var version = await pages.GetLivePageAsync(site.Id, templatePageId, cancellationToken).ConfigureAwait(false);
        if (version is null)
        {
            return null;
        }

        var item = (await items.GetAsync(site.Id, [itemId], cancellationToken).ConfigureAwait(false)).GetValueOrDefault(itemId);
        return item is null
            ? null
            : await RenderVersionAsync(site, templatePageId, version, basePath, item, request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<RenderedPage> RenderVersionAsync(
        SiteDto site, Guid pageId, LivePageVersion version, string basePath, PublishedItem? pageItem, ListRequest request, CancellationToken cancellationToken)
    {
        var design = await designs.GetLiveAsync(site.Id, cancellationToken).ConfigureAwait(false);

        var references = new RenderReferences();
        references.Collect(version.Document);

        var components = await pages.GetLiveComponentsAsync(site.Id, references.Components, cancellationToken).ConfigureAwait(false);
        foreach (var component in components.Values)
        {
            references.Collect(component.Document);
        }

        // 016 TB-09: what the bindings read, and what their values show and link to.
        var fetched = await bindings.FetchAsync(
                site.Id, [version.Document, .. components.Values.Select(c => c.Document)], pageItem, request.Params.Pages, request.Path, cancellationToken)
            .ConfigureAwait(false);
        if (fetched.SkippedLists is { Count: > 0 } skipped)
        {
            // TB-09: ids only, never values (FR-038).
            logger.LogWarning(
                "Page {PageId} of site {SiteId} needed more than {Budget} list queries; lists {ListIds} show their empty state.",
                pageId,
                site.Id,
                ListQueries.MaxQueriesPerRender,
                string.Join(", ", skipped.Distinct(StringComparer.Ordinal)));
        }

        references.Pages.UnionWith(fetched.Pages);
        references.Media.UnionWith(fetched.Media);

        // 017 research SE-05: the sharing image, the site's default one, fetched with the rest.
        var seo = SeoDocument.From(version.Document);
        references.Media.UnionWith(HeadBuilder.MediaIds(seo, site));

        var targets = await pages.GetLinkTargetsAsync(site.Id, references.Pages, cancellationToken).ConfigureAwait(false);
        var files = await media.GetAsync(site.Id, references.Media, cancellationToken).ConfigureAwait(false);

        // 016 FR-030: an item page is titled by the item's name, else by the template's title.
        var title = string.IsNullOrWhiteSpace(pageItem?.Name) ? version.Title : pageItem.Name;

        var inputs = new RenderInputs(
            title, version.Document, design.Document, components, targets, files, site.DefaultLanguage, basePath, fetched.Data,
            seo, site, request.Path, pageItem is not null);
        metrics.Rendered();
        return Render(inputs, renderer, context => LogBroken(context, site.Id, pageId));
    }

    /// <summary>One warning per render that met broken bindings: node ids and reasons, never values (016 FR-038).</summary>
    private void LogBroken(RenderContext context, Guid siteId, Guid pageId)
    {
        if (context.BrokenBindings.Count > 0)
        {
            logger.LogWarning(
                "Page {PageId} of site {SiteId} rendered with {Count} broken binding(s): {Bindings}.",
                pageId,
                siteId,
                context.BrokenBindings.Count,
                string.Join(", ", context.BrokenBindings.Select(b => $"{b.NodeId}:{b.Reason}")));
        }
    }

    /// <summary>A page from its inputs: the body, the shell, and the style hash its policy allows.</summary>
    public static RenderedPage Render(RenderInputs inputs, PageRenderer renderer, Action<RenderContext>? afterRender = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(renderer);

        var context = new RenderContext(inputs, BlockDefaults.Current);
        var body = new HtmlWriter();
        renderer.RenderDocument(inputs.Document, PageRenderer.PageScope, context, body);
        afterRender?.Invoke(context);

        var head = inputs.Site is { } site
            ? HeadBuilder.Build(new HeadInputs(inputs.Title, inputs.Path, inputs.IsItemPage, inputs.Seo, site, inputs.Media))
            : null;
        var document = PageShell.Write(inputs.Title, inputs.Language, inputs.BasePath, context.Tokens, context.NodeRules, body.ToString(), head);
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

/// <summary>
/// What a render needs of the request for its lists (016 TB-11): the address its pager links
/// keep, and the page each paginated list shows.
/// </summary>
internal sealed record ListRequest(string Path, PageParams Params);
