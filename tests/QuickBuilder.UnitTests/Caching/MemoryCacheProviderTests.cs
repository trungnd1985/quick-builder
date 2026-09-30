using Microsoft.Extensions.Options;
using QuickBuilder.Shared.Infrastructure.Caching;

namespace QuickBuilder.UnitTests.Caching;

/// <summary>
/// The in-memory cache is bounded, single-flight and site-namespaced (012 research PR-07).
/// </summary>
public sealed class MemoryCacheProviderTests
{
    private const long Megabyte = 1024 * 1024;

    private static MemoryCacheProvider Create(int megabytes = 16) =>
        new(Options.Create(new CacheOptions { MemoryLimitMegabytes = megabytes }));

    [Fact]
    public async Task A_hit_does_not_run_the_factory()
    {
        using var cache = Create();
        var calls = 0;

        for (var i = 0; i < 3; i++)
        {
            await cache.GetOrCreateAsync("k", _ => Task.FromResult(++calls), _ => new CacheEntrySettings(1));
        }

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Concurrent_misses_for_one_key_share_one_factory_call()
    {
        using var cache = Create();
        var calls = 0;
        var gate = new TaskCompletionSource();

        async Task<int> Factory(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            await gate.Task;
            return 42;
        }

        var callers = Enumerable.Range(0, 10)
            .Select(_ => cache.GetOrCreateAsync("k", Factory, _ => new CacheEntrySettings(1)))
            .ToList();

        gate.SetResult();
        var results = await Task.WhenAll(callers);

        Assert.Equal(1, calls);
        Assert.All(results, r => Assert.Equal(42, r));
    }

    [Fact]
    public async Task A_failed_factory_is_not_cached()
    {
        using var cache = Create();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cache.GetOrCreateAsync<int>("k", _ => throw new InvalidOperationException(), _ => new CacheEntrySettings(1)));

        var value = await cache.GetOrCreateAsync("k", _ => Task.FromResult(7), _ => new CacheEntrySettings(1));

        Assert.Equal(7, value);
    }

    [Fact]
    public async Task Entries_beyond_the_size_limit_evict_older_ones()
    {
        using var cache = Create(megabytes: 16);

        // Twenty 1 MB entries into a 16 MB cache: some of the earliest must go.
        for (var i = 0; i < 20; i++)
        {
            await cache.GetOrCreateAsync($"k{i}", _ => Task.FromResult(i), _ => new CacheEntrySettings(Megabyte));
        }

        // Compaction runs in the background; give it a moment, then count what survived.
        await Task.Delay(200);

        var calls = 0;
        for (var i = 0; i < 20; i++)
        {
            await cache.GetOrCreateAsync($"k{i}", _ => Task.FromResult(++calls), _ => new CacheEntrySettings(1));
        }

        Assert.True(calls >= 4, $"Expected at least 4 evictions from a 16 MB cache holding 20 MB, got {calls}.");
    }

    [Fact]
    public async Task Sliding_expiry_drops_an_entry_nobody_reads()
    {
        using var cache = Create();

        await cache.GetOrCreateAsync(
            "k", _ => Task.FromResult(1), _ => new CacheEntrySettings(1, Sliding: TimeSpan.FromMilliseconds(50)));

        await Task.Delay(150);

        var value = await cache.GetOrCreateAsync("k", _ => Task.FromResult(2), _ => new CacheEntrySettings(1));

        Assert.Equal(2, value);
    }

    [Fact]
    public void Keys_of_two_sites_never_collide()
    {
        var a = CacheKey.ForSite(Guid.NewGuid(), "page", "same");
        var b = CacheKey.ForSite(Guid.NewGuid(), "page", "same");

        Assert.NotEqual(a, b);
        Assert.StartsWith("site:", a, StringComparison.Ordinal);
    }
}
