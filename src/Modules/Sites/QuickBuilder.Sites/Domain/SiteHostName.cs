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
/// <para>
/// What counts as a host name, and its stored spelling, is decided by
/// <see cref="Shared.Kernel.MultiSite.HostNames"/> alone (009 research S-04). This type
/// only ever holds a name that has already been through it.
/// </para>
/// </remarks>
internal sealed class SiteHostName : ISiteOwned
{
    public const int MaxLength = Shared.Kernel.MultiSite.HostNames.MaxLength;

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

    /// <summary>The normalised, ASCII form.</summary>
    public string HostName { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    internal static SiteHostName Create(Guid siteId, string normalisedHostName, DateTimeOffset createdAtUtc) =>
        new(Guid.CreateVersion7(), siteId, normalisedHostName, createdAtUtc);
}
