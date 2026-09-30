using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Rendering.Contracts;

/// <summary>
/// The one not-found answer for a resolved site (public-rendering.md §4, research PR-08).
/// </summary>
/// <remarks>
/// The host writes it for public paths nothing answers and for administrative paths on a
/// client domain (009 research S-13). One writer for both is what keeps them byte-identical,
/// so the administrative surface stays invisible on client domains.
/// </remarks>
public interface IPublicNotFoundPage
{
    Task<PublicResponse> RenderAsync(SiteDto site, CancellationToken cancellationToken = default);
}
