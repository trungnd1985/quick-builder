using System.ComponentModel.DataAnnotations;

namespace QuickBuilder.Shared.Infrastructure.Caching;

/// <summary>
/// Settings of the in-memory cache (012 research PR-07).
/// </summary>
/// <remarks>
/// Bound from the <c>Cache</c> section and validated at startup: a value outside the range
/// stops the process before it listens, naming the key.
/// </remarks>
public sealed class CacheOptions
{
    public const string SectionName = "Cache";

    /// <summary>
    /// Upper bound on the memory the cache may hold, per instance. Rendered pages dominate it.
    /// </summary>
    [Range(16, 16384)]
    public int MemoryLimitMegabytes { get; set; } = 256;
}
