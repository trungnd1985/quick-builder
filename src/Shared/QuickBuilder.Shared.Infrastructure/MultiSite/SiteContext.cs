namespace QuickBuilder.Shared.Infrastructure.MultiSite;

/// <summary>
/// The site this request concerns, if any (FR-011a, FR-011b).
/// </summary>
/// <remarks>
/// Populated by the public surface's site-resolution middleware from the Host header, so
/// handlers never read request headers themselves.
/// <para>
/// <see cref="SiteId"/> is deliberately nullable. The administrative surface has no
/// resolved site and must still work on an installation with zero sites (FR-011b,
/// FR-014a), so "no site" is a normal state rather than an error. Query filters treat a
/// null site as matching nothing, which fails closed.
/// </para>
/// </remarks>
public interface ISiteContext
{
    Guid? SiteId { get; }

    bool HasSite { get; }
}

/// <summary>Scoped, mutable implementation. Only the resolution middleware sets it.</summary>
internal sealed class SiteContext : ISiteContext, ISiteContextSetter
{
    public Guid? SiteId { get; private set; }

    public bool HasSite => SiteId.HasValue;

    public void SetSite(Guid siteId)
    {
        if (SiteId.HasValue)
        {
            throw new InvalidOperationException(
                "The site for this request has already been resolved. Resolution happens " +
                "once, in middleware, before anything else runs.");
        }

        SiteId = siteId;
    }
}

/// <summary>
/// Write side of <see cref="ISiteContext"/>, kept separate so that only the resolution
/// middleware can set the site and handlers can only read it.
/// </summary>
public interface ISiteContextSetter
{
    void SetSite(Guid siteId);
}
