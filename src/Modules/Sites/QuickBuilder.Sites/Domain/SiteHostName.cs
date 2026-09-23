using System.Text.RegularExpressions;
using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Sites.Domain;

/// <summary>
/// A host name that maps a public request to one site.
/// </summary>
/// <remarks>
/// The worked example of a site-owned record (FR-013): it implements
/// <see cref="ISiteOwned"/>, so the global query filter applies to it automatically and
/// no handler filters by site by hand.
/// <para>
/// Uniqueness is global, not per site. Resolution goes host name → site, so two sites
/// claiming one host name would make the mapping ambiguous. The unique index makes that
/// unrepresentable; the handler turns the resulting constraint violation into an expected
/// failure rather than an unhandled exception.
/// </para>
/// </remarks>
internal sealed partial class SiteHostName : ISiteOwned
{
    public const int MaxLength = 253;

    private SiteHostName(Guid id, Guid siteId, string hostName, DateTimeOffset createdAtUtc)
    {
        Id = id;
        SiteId = siteId;
        HostName = hostName;
        CreatedAtUtc = createdAtUtc;
    }

    // EF Core materialisation.
    private SiteHostName()
    {
        HostName = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid SiteId { get; private set; }

    public string HostName { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    internal static SiteHostName Create(Guid siteId, string normalisedHostName, DateTimeOffset createdAtUtc) =>
        new(Guid.CreateVersion7(), siteId, normalisedHostName, createdAtUtc);

    /// <summary>Lowercases and trims. Storage is citext, but normalising on the way in
    /// keeps comparisons and cache keys predictable regardless of the column type.</summary>
    internal static string Normalise(string? hostName) =>
        hostName?.Trim().ToLowerInvariant() ?? string.Empty;

    /// <summary>
    /// Labels of letters, digits and hyphens, no leading or trailing hyphen, 253
    /// characters at most.
    /// </summary>
    /// <remarks>
    /// A port is deliberately rejected: ports are the reverse proxy's concern (D-12), and
    /// accepting one here would mean two spellings of the same site.
    /// </remarks>
    internal static bool IsValid(string hostName) =>
        !string.IsNullOrEmpty(hostName)
        && hostName.Length <= MaxLength
        && HostNamePattern().IsMatch(hostName);

    [GeneratedRegex(
        @"^(?=.{1,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)(\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex HostNamePattern();
}
