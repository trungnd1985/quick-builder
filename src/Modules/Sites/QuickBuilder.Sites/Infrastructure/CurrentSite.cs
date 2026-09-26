using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Sites.Infrastructure;

/// <summary>
/// The resolved site's details for this request (009 FR-015, research S-02).
/// </summary>
/// <remarks>
/// Returns the instance the resolution middleware loaded through this request's
/// <see cref="SiteDirectory"/>, so reading it costs nothing. Null whenever no site is
/// resolved — every administrative request.
/// </remarks>
internal sealed class CurrentSite(ISiteContext siteContext, SiteDirectory directory) : ICurrentSite
{
    public SiteDto? Site => siteContext.SiteId is { } id ? directory.Loaded(id) : null;
}
