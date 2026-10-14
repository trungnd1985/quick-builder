using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Pages.Domain.Redirects;

/// <summary>
/// How often a redirect was followed, and when last (017 FR-021a, research SE-12).
/// </summary>
/// <remarks>
/// Kept apart from <see cref="Redirect"/> so that saving counts never touches the redirect's
/// <c>xmin</c>, and a member editing it is never refused because a visitor followed it. Only
/// the background flusher writes it; serving a redirect never does (Principle VII).
/// </remarks>
internal sealed class RedirectUsage : ISiteOwned
{
    public RedirectUsage(Guid redirectId, Guid siteId, long hits, DateTimeOffset lastUsedAtUtc)
    {
        RedirectId = redirectId;
        SiteId = siteId;
        Hits = hits;
        LastUsedAtUtc = lastUsedAtUtc;
    }

    // EF Core materialisation.
    private RedirectUsage()
    {
    }

    public Guid RedirectId { get; private set; }

    public Guid SiteId { get; private set; }

    public long Hits { get; private set; }

    public DateTimeOffset LastUsedAtUtc { get; private set; }
}
