using Microsoft.Extensions.Caching.Memory;

namespace QuickBuilder.Shared.Infrastructure.Caching;

/// <summary>
/// The cache abstraction (constitution Principle IV).
/// </summary>
/// <remarks>
/// Deliberately narrow. Principle IV requires providers behind an abstraction; Principle
/// XI forbids abstractions nothing needs. This one has a real consumer today — host-name
/// resolution runs on every public request — and a real second implementation later
/// (Redis, for multi-instance deployments).
/// <para>
/// No media-storage abstraction ships in this feature for the same reason inverted:
/// nothing stores a file yet. See plan.md exception E-2.
/// </para>
/// <para>
/// Known limitation (research.md D-09): the in-memory default cannot invalidate across
/// instances. Correct for the single-instance reference deployment; the Redis provider
/// spec must solve cross-instance eviction before multi-instance is advertised.
/// </para>
/// </remarks>
public interface ICacheProvider
{
    Task<T?> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? absoluteExpiration = null,
        CancellationToken cancellationToken = default);

    void Remove(string key);
}

/// <summary>
/// Builds cache keys namespaced by site (constitution Principle V).
/// </summary>
public static class CacheKey
{
    /// <summary>A key scoped to one site, so two sites can never collide.</summary>
    public static string ForSite(Guid siteId, string category, string discriminator) =>
        $"site:{siteId:N}:{category}:{discriminator}";

    /// <summary>
    /// A key for data that legitimately spans sites, such as the host-name lookup table
    /// used before any site is known.
    /// </summary>
    public static string Global(string category, string discriminator) =>
        $"global:{category}:{discriminator}";
}

internal sealed class MemoryCacheProvider(IMemoryCache cache) : ICacheProvider
{
    private static readonly TimeSpan DefaultExpiration = TimeSpan.FromMinutes(5);

    public async Task<T?> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? absoluteExpiration = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);

        if (cache.TryGetValue(key, out T? cached))
        {
            return cached;
        }

        var value = await factory(cancellationToken).ConfigureAwait(false);

        cache.Set(key, value, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = absoluteExpiration ?? DefaultExpiration,
        });

        return value;
    }

    public void Remove(string key) => cache.Remove(key);
}
