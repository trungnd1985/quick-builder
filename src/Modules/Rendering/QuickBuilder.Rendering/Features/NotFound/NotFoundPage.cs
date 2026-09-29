using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuickBuilder.Editor.Contracts;
using QuickBuilder.Media.Contracts;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Rendering.Contracts;
using QuickBuilder.Rendering.Domain;
using QuickBuilder.Rendering.Features.RenderPage;
using QuickBuilder.Rendering.Rendering;
using QuickBuilder.Shared.Infrastructure.Caching;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Rendering.Features.NotFound;

/// <summary>
/// The site's not-found page (012 FR-030, research PR-08, PR-13; public-rendering.md §4).
/// </summary>
/// <remarks>
/// The same page for every missing address on a site — no page, an unpublished page, a
/// draft-only page, and the administrative prefixes on a client domain — so it never tells a
/// visitor which one it was. It is built from the site alone: nothing of the request, not even
/// the base path, which comes from the site's base URL.
/// <para>
/// It is a small fixed document drawn by the page renderer, so it wears the site's live
/// design exactly as its pages do, but shows no components (clarification Q4). The bytes are
/// cached under everything they depend on, so a flood of random addresses costs a lookup
/// each, not a render.
/// </para>
/// </remarks>
internal sealed class NotFoundPage(
    IPublishedContentReader pages,
    IPublishedDesignReader designs,
    PageRenderer renderer,
    ICacheProvider cache,
    ILogger<NotFoundPage> logger) : IPublicNotFoundPage
{
    /// <summary>Stands for the home page in the fixed document's one link.</summary>
    private static readonly Guid Home = new("00000000-0000-0000-0000-00000000000f");

    private static readonly IReadOnlyDictionary<Guid, LiveDocument> NoComponents = new Dictionary<Guid, LiveDocument>();

    private static readonly IReadOnlyDictionary<Guid, PublicMediaFile> NoMedia = new Dictionary<Guid, PublicMediaFile>();

    public async Task<PublicResponse> RenderAsync(SiteDto site, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(site);

        var homeLive = await pages.IsHomePageLiveAsync(site.Id, cancellationToken).ConfigureAwait(false);
        var design = await designs.GetLiveAsync(site.Id, cancellationToken).ConfigureAwait(false);
        var basePath = PageShell.TrimBase(new Uri(site.BaseUrl).AbsolutePath);

        var key = CacheKey.ForSite(
            site.Id, "notfound", $"d{design.Number}:s{site.Version}:h{(homeLive ? 1 : 0)}:b{basePath}:r{RenderKey.RendererStamp}");

        RenderedPage page;
        try
        {
            page = await cache.GetOrCreateAsync(
                    key,
                    _ => Task.FromResult(Render(site, design.Document, homeLive, basePath)),
                    rendered => new CacheEntrySettings(rendered.Html.Length),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The page cache failed; the not-found page of site {SiteId} was rendered without it.", site.Id);
            page = Render(site, design.Document, homeLive, basePath);
        }

        var response = PageResponder.ToResponse(page, new Dictionary<string, string> { ["Cache-Control"] = "no-store" });
        return response with { StatusCode = StatusCodes.Status404NotFound };
    }

    private RenderedPage Render(SiteDto site, JsonElement design, bool homeLive, string basePath)
    {
        var texts = PublicTexts.For(site.DefaultLanguage);
        using var document = JsonDocument.Parse(Document(texts, homeLive));

        var targets = homeLive
            ? new Dictionary<Guid, PageLinkTarget> { [Home] = new("/", IsLive: true) }
            : new Dictionary<Guid, PageLinkTarget>();

        var inputs = new RenderInputs(
            texts.Title, document.RootElement, design, NoComponents, targets, NoMedia, site.DefaultLanguage, basePath);
        return PageResponder.Render(inputs, renderer);
    }

    /// <summary>A Section with the heading, the sentence and, while the home page is live, a link home.</summary>
    private static string Document(PublicTexts texts, bool homeLive)
    {
        var button = homeLive
            ? $$$""",{"id":"home","type":"button","props":{"label":{{{JsonSerializer.Serialize(texts.Home)}}},"link":{"type":"page","pageId":"{{{Home}}}"}},"children":[]}"""
            : string.Empty;

        return $$$"""
            {"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[
              {"id":"notfound","type":"section","props":{},"children":[
                {"id":"title","type":"heading","props":{"text":{{{JsonSerializer.Serialize(texts.Title)}}},"level":1},"children":[]},
                {"id":"message","type":"text","props":{"text":{{{JsonSerializer.Serialize(texts.Message)}}}},"children":[]}{{{button}}}]}]}}
            """;
    }
}
