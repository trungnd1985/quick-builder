using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace QuickBuilder.Shared.Infrastructure.Caching;

/// <summary>
/// The cache abstraction (constitution Principle IV).
/// </summary>
/// <remarks>
/// Deliberately narrow. Principle IV requires providers behind an abstraction; Principle
/// XI forbids abstractions nothing needs. Its consumer is the renderer's output cache
/// (012 research PR-07).
/// <para>
/// <b>Correct across instances without invalidation.</b> Nothing ever relies on removing an
/// entry to be correct. Rendering keys each page by generations read from the database on
/// every request (012 research PR-05), so a change produces a new key on every instance at
/// its next request, whatever the provider. That is what closed exception E-4: a shared
/// provider would only save work, never fix staleness.
/// </para>
/// </remarks>
public interface ICacheProvider
{
    /// <summary>
    /// The cached value for <paramref name="key"/>, or the factory's result, stored with
    /// <paramref name="settings"/>. Concurrent misses for one key share one factory call.
    /// </summary>
    Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        Func<T, CacheEntrySettings> settings,
        CancellationToken cancellationToken = default);

    void Remove(string key);
}

/// <summary>How one entry is kept.</summary>
/// <param name="Size">Its size in bytes, counted against <c>Cache:MemoryLimitMegabytes</c>.</param>
/// <param name="Sliding">Dropped after this long without a read, if set.</param>
/// <param name="Absolute">Dropped this long after it was stored, if set.</param>
public sealed record CacheEntrySettings(long Size, TimeSpan? Sliding = null, TimeSpan? Absolute = null);

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

/// <summary>
/// The in-memory provider: a size-bounded cache per process (012 research PR-07).
/// </summary>
/// <remarks>
/// It owns its <see cref="MemoryCache"/> rather than sharing the host's, because every entry
/// must carry a size once a size limit is set, and nothing else in the process should have to
/// know that. When the limit is reached the least recently used entries go first.
/// </remarks>
internal sealed class MemoryCacheProvider : ICacheProvider, IDisposable
{
    private readonly MemoryCache _cache;

    private readonly ConcurrentDictionary<string, Lazy<Task<object?>>> _inFlight = new(StringComparer.Ordinal);

    public MemoryCacheProvider(IOptions<CacheOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _cache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = options.Value.MemoryLimitMegabytes * 1024L * 1024L,
            // Free a fifth of the limit when it is reached, so a full cache does not compact
            // on every insert.
            CompactionPercentage = 0.2,
        });
    }

    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        Func<T, CacheEntrySettings> settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(settings);

        if (_cache.TryGetValue(key, out T? cached))
        {
            return cached!;
        }

        // Single-flight: the first miss creates the task, every concurrent miss awaits it.
        // The factory runs without the caller's token, because other callers are waiting on
        // it too; one caller giving up must not fail the others.
        var lazy = _inFlight.GetOrAdd(
            key,
            missingKey => new Lazy<Task<object?>>(
                async () =>
                {
                    try
                    {
                        var value = await factory(CancellationToken.None).ConfigureAwait(false);
                        Store(missingKey, value, settings(value));
                        return value;
                    }
                    finally
                    {
                        _inFlight.TryRemove(missingKey, out _);
                    }
                },
                LazyThreadSafetyMode.ExecutionAndPublication));

        var result = await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        return (T)result!;
    }

    public void Remove(string key) => _cache.Remove(key);

    public void Dispose() => _cache.Dispose();

    private void Store<T>(string key, T value, CacheEntrySettings entry)
    {
        var options = new MemoryCacheEntryOptions
        {
            // A size of at least one keeps zero-length values countable.
            Size = Math.Max(1, entry.Size),
            SlidingExpiration = entry.Sliding,
            AbsoluteExpirationRelativeToNow = entry.Absolute,
        };

        _cache.Set(key, value, options);
    }
}
