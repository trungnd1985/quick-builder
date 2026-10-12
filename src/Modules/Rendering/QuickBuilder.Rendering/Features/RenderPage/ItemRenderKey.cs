namespace QuickBuilder.Rendering.Features.RenderPage;

/// <summary>
/// Everything an item page depends on, as numbers read before its content (016 research TB-10,
/// data-model §6).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The template's <c>generation</c> moves on with every change to the template, to what
/// it shows and to what it links to, as a page's does (012 PR-05).</item>
/// <item>The item's render stamp moves on with every publish, unpublish and rollback of the
/// item, of an item it refers to, and of a media file it shows (TB-10).</item>
/// <item>The design number, the site's version and the renderer stamp, as for pages.</item>
/// </list>
/// Publishing one item changes only that item's key, so every other item page stays cached
/// (SC-006). The key is read before the content, so the content cached under it is at least as
/// new as the key.
/// </remarks>
internal sealed record ItemRenderKey(
    Guid SiteId,
    Guid TemplatePageId,
    long TemplateGeneration,
    Guid ItemId,
    long RenderStamp,
    int DesignNumber,
    uint SiteVersion,
    string BasePath,
    string PageParams)
{
    /// <summary>The key's string form, scoped to the site (constitution Principle V).</summary>
    public string CacheKey =>
        Shared.Infrastructure.Caching.CacheKey.ForSite(
            SiteId,
            "item",
            $"{TemplatePageId:N}:{TemplateGeneration}:{ItemId:N}:{RenderStamp}:{DesignNumber}:{SiteVersion}:{BasePath}:{PageParams}:{RenderKey.RendererStamp}");

    /// <summary>A strong validator: the same key always renders the same bytes.</summary>
    public string ETag => RenderKey.ETagOf(CacheKey);

    /// <summary>Whether an <c>If-None-Match</c> header names this key's ETag.</summary>
    public bool IsMatchedBy(string? ifNoneMatch) => RenderKey.Matches(ETag, ifNoneMatch);
}
