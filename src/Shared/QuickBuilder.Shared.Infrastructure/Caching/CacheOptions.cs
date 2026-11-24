using System.ComponentModel.DataAnnotations;
using QuickBuilder.Shared.Kernel.Settings;

namespace QuickBuilder.Shared.Infrastructure.Caching;

/// <summary>
/// Settings of the in-memory cache (012 research PR-07).
/// </summary>
/// <remarks>
/// Bound from the <c>Cache</c> section and validated at startup: a value outside the range
/// stops the process before it listens, naming the key.
/// </remarks>
[SettingsSection(SectionName, Area = "cache", Title = "Cache")]
public sealed class CacheOptions
{
    public const string SectionName = "Cache";

    /// <summary>
    /// Upper bound on the memory the cache may hold, per instance. Rendered pages dominate it.
    /// </summary>
    [Range(16, 16384, ErrorMessage = "Cache:MemoryLimitMegabytes must be between {1} and {2}.")]
    [Setting("Upper bound on each instance's in-memory cache, in megabytes. Rendered public pages take nearly all of it; when full, the least recently used pages are dropped and rendered again on their next request.")]
    public int MemoryLimitMegabytes { get; set; } = 256;
}
