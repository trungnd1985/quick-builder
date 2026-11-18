using QuickBuilder.Shared.Infrastructure.Caching;

namespace QuickBuilder.Rendering.Features.Preview;

/// <summary>
/// The media files a link's target uses (022 research PV-11, spec FR-027): a link opens those
/// files and no other file of the site.
/// </summary>
/// <remarks>
/// <para>
/// The set is what the last render of the target referenced. It is kept for 5 minutes under
/// <c>site:{id}:preview-media:{link}</c>, so the images of one page do not each cost a render.
/// The entry holds ids only, never output, and is apart from every render cache key (FR-023).
/// </para>
/// <para>
/// A page render replaces it, so a reload after a draft save is never refused its new images. A
/// file not in the set gets one fresh computation before it is refused, which covers an entry
/// kept on another instance or evicted.
/// </para>
/// </remarks>
internal sealed class PreviewMediaSet(ICacheProvider cache)
{
    private static readonly TimeSpan Kept = TimeSpan.FromMinutes(5);

    /// <summary>Stores what a page render just referenced.</summary>
    public async Task RememberAsync(Guid siteId, Guid linkId, IReadOnlySet<Guid> media, CancellationToken cancellationToken)
    {
        var key = Key(siteId, linkId);
        cache.Remove(key);
        await cache.GetOrCreateAsync(key, _ => Task.FromResult(media), Settings, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Whether the target uses <paramref name="fileId"/>, computing the set with
    /// <paramref name="compute"/> when it is not kept, or once more when the file is not in it.
    /// </summary>
    public async Task<bool> ContainsAsync(
        Guid siteId, Guid linkId, Guid fileId, Func<CancellationToken, Task<IReadOnlySet<Guid>>> compute, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compute);

        var key = Key(siteId, linkId);
        var kept = await cache.GetOrCreateAsync(key, compute, Settings, cancellationToken).ConfigureAwait(false);
        if (kept.Contains(fileId))
        {
            return true;
        }

        cache.Remove(key);
        var fresh = await cache.GetOrCreateAsync(key, compute, Settings, cancellationToken).ConfigureAwait(false);
        return fresh.Contains(fileId);
    }

    private static string Key(Guid siteId, Guid linkId) => CacheKey.ForSite(siteId, "preview-media", linkId.ToString("N"));

    private static CacheEntrySettings Settings(IReadOnlySet<Guid> media) => new(64 + (media.Count * 16), Absolute: Kept);
}
