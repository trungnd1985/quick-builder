using System.Text;
using QuickBuilder.Rendering.Contracts;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Rendering.Features.NotFound;

/// <summary>
/// The site's not-found answer (012 research PR-08, PR-13; public-rendering.md §4).
/// </summary>
internal sealed class NotFoundPage : IPublicNotFoundPage
{
    private static readonly byte[] Body = Encoding.UTF8.GetBytes(
        """{"type":"https://quickbuilder.dev/errors/public-not-found","title":"Not found","status":404,"errorCode":"public.not_found"}""");

    private static readonly IReadOnlyDictionary<string, string> NoHeaders = new Dictionary<string, string>();

    public Task<PublicResponse> RenderAsync(SiteDto site, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(site);

        return Task.FromResult(new PublicResponse(StatusCodes.Status404NotFound, "application/problem+json", NoHeaders, Body));
    }
}
