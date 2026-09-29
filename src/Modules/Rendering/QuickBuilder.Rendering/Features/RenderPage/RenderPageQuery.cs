using QuickBuilder.Rendering.Contracts;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Rendering.Features.RenderPage;

/// <summary>A visitor's request for an address on a resolved site.</summary>
/// <param name="Site">The site the host name resolved to.</param>
/// <param name="Path">The request path, base path removed, as the server decoded it.</param>
/// <param name="BasePath">The installation's base path, prefixed to every address written.</param>
internal sealed record RenderPageQuery(SiteDto Site, string? Path, string BasePath) : IQuery<PublicResponse>;

/// <summary>The page at the address, or the site's not-found page (public-rendering.md §1–§4).</summary>
internal sealed class RenderPageHandler(PageResponder responder, IPublicNotFoundPage notFound)
    : IQueryHandler<RenderPageQuery, PublicResponse>
{
    public async Task<PublicResponse> HandleAsync(RenderPageQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return PathNormaliser.Normalise(query.Path) is { } path
            ? await responder.RespondAsync(query.Site, path, query.BasePath, cancellationToken).ConfigureAwait(false)
            : await notFound.RenderAsync(query.Site, cancellationToken).ConfigureAwait(false);
    }
}
