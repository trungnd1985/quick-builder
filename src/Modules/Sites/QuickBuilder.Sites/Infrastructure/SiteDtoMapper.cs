using System.Linq.Expressions;
using QuickBuilder.Shared.Kernel.MultiSite;
using QuickBuilder.Sites.Contracts;
using QuickBuilder.Sites.Domain;

namespace QuickBuilder.Sites.Infrastructure;

/// <summary>
/// The one way a site becomes a <see cref="SiteDto"/> (009 research S-17).
/// </summary>
/// <remarks>
/// Two steps, because the base URL depends on configuration the database cannot see:
/// <see cref="Projection"/> is translated to SQL and loads exactly the columns needed,
/// and <see cref="ToDto"/> finishes the job in memory. Every read path — list, get,
/// resolution, and the write paths returning the saved site — goes through both, so no
/// two endpoints can disagree about a site's shape.
/// </remarks>
internal sealed class SiteDtoMapper(SitesOptions options)
{
    /// <summary>What the database returns for one site.</summary>
    internal sealed record SiteRow(
        Guid Id,
        string Name,
        string Scheme,
        string DefaultLanguage,
        string Status,
        string PrimaryHostName,
        uint Version,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc,
        List<HostNameRow> HostNames);

    internal sealed record HostNameRow(string HostName, DateTimeOffset CreatedAtUtc);

    internal static Expression<Func<Site, SiteRow>> Projection { get; } = s => new SiteRow(
        s.Id,
        s.Name,
        s.Scheme,
        s.DefaultLanguage,
        s.Status,
        s.PrimaryHostName,
        s.Version,
        s.CreatedAtUtc,
        s.UpdatedAtUtc,
        s.HostNames.Select(h => new HostNameRow(h.HostName, h.CreatedAtUtc)).ToList());

    private static readonly Lazy<Func<Site, SiteRow>> CompiledProjection = new(() => Projection.Compile());

    /// <summary>Maps a site already in memory, such as one just saved.</summary>
    public SiteDto ToDto(Site site)
    {
        ArgumentNullException.ThrowIfNull(site);
        return ToDto(CompiledProjection.Value(site));
    }

    public SiteDto ToDto(SiteRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new SiteDto(row.Id, row.Name, OrderedHostNames(row), row.CreatedAtUtc)
        {
            PrimaryHostName = row.PrimaryHostName,
            Scheme = row.Scheme,
            BaseUrl = BaseUrl(row.Scheme, row.PrimaryHostName),
            DefaultLanguage = row.DefaultLanguage,
            Status = row.Status,
            Version = row.Version,
            UpdatedAtUtc = row.UpdatedAtUtc,
        };
    }

    /// <summary>
    /// <c>{scheme}://{primary}{base path}</c>, or with a trailing slash at the root
    /// (research S-07). No port: host names never carry one.
    /// </summary>
    internal string BaseUrl(string scheme, string primaryHostName)
    {
        // An IPv6 literal needs its brackets back to be a URL.
        var host = primaryHostName.Contains(':', StringComparison.Ordinal)
            ? $"[{primaryHostName}]"
            : primaryHostName;

        return options.BasePath.Length == 0
            ? $"{scheme}://{host}/"
            : $"{scheme}://{host}{options.BasePath}";
    }

    /// <summary>The primary first, then the rest in the order they were added.</summary>
    private static List<string> OrderedHostNames(SiteRow row) =>
        row.HostNames
            .OrderByDescending(h => string.Equals(h.HostName, row.PrimaryHostName, StringComparison.Ordinal))
            .ThenBy(h => h.CreatedAtUtc)
            .ThenBy(h => h.HostName, StringComparer.Ordinal)
            .Select(h => h.HostName)
            .ToList();

    /// <summary>The readable form of each host name, for the admin shell (FR-009).</summary>
    internal static string Display(string hostName) => HostNames.ToDisplay(hostName);
}
