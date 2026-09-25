using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Sites;

/// <summary>
/// The two hosting settings the Sites module needs (009 research S-07, S-13).
/// </summary>
/// <remarks>
/// Both keys belong to the host's <c>Hosting</c> section, which validates them at startup
/// and fails naming the key. This module only reads them — as Identity already reads
/// <c>Hosting:BasePath</c> for its links — so it silently skips an admin host name the
/// host would refuse anyway.
/// </remarks>
internal sealed class SitesOptions
{
    /// <summary><c>Hosting:BasePath</c>, without a trailing slash. Empty for the root.</summary>
    public string BasePath { get; set; } = string.Empty;

    /// <summary><c>Hosting:AdminHostNames</c>, normalised.</summary>
    public IReadOnlySet<string> AdminHostNames { get; set; } = new HashSet<string>(StringComparer.Ordinal);

    internal static SitesOptions From(Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var adminHostNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in configuration.GetSection("Hosting:AdminHostNames").GetChildren())
        {
            if (HostNames.TryNormalise(entry.Value, out var normalised))
            {
                adminHostNames.Add(normalised);
            }
        }

        return new SitesOptions
        {
            BasePath = (configuration["Hosting:BasePath"] ?? string.Empty).TrimEnd('/'),
            AdminHostNames = adminHostNames,
        };
    }

    public bool IsAdminHostName(string normalisedHostName) => AdminHostNames.Contains(normalisedHostName);
}
