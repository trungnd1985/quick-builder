using QuickBuilder.Cms.Contracts.Publishing;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Rendering.Contracts;
using QuickBuilder.Rendering.Features.RenderPage;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Rendering.Features.FormResponse;

/// <summary>
/// The page shown when a submission without scripts is refused (018 research FS-09;
/// public-forms.md §2.4): the address the form was sent from, rendered with the visitor's values
/// and what went wrong, never cached and never shown to anyone else (FR-014).
/// </summary>
/// <remarks>
/// The address is matched as a visitor's request is (a live page, then an item page under a live
/// template), and the render goes through <see cref="PageResponder"/>'s own steps, so the page is
/// the same as the cached one apart from that form's state. Redirects are not followed: a form
/// is only ever sent from an address that served it.
/// </remarks>
internal sealed class FormResponsePage(IPublishedContentReader pages, IPublishedItemReader items, PageResponder responder) : IFormResponsePage
{
    public async Task<PublicResponse?> RenderAsync(
        SiteDto site, string path, string basePath, FormState state, int statusCode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(state);

        var rendered = await RenderPageAsync(site, path, basePath, state, cancellationToken).ConfigureAwait(false);
        if (rendered is null)
        {
            return null;
        }

        var page = PageResponder.ToResponse(rendered);
        var headers = new Dictionary<string, string>(page.Headers, StringComparer.OrdinalIgnoreCase)
        {
            ["Cache-Control"] = "no-store",
        };
        if (state.RetryAfterSeconds is { } retryAfter)
        {
            headers["Retry-After"] = retryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return page with { StatusCode = statusCode, Headers = headers };
    }

    private async Task<Domain.RenderedPage?> RenderPageAsync(SiteDto site, string path, string basePath, FormState state, CancellationToken cancellationToken)
    {
        if (await pages.FindLiveByPathAsync(site.Id, path, cancellationToken).ConfigureAwait(false) is { } live)
        {
            var request = new ListRequest(path, PageParams.From(live.Params, query: null));
            return await responder.RenderAsync(site, live.PageId, basePath, request, cancellationToken, state).ConfigureAwait(false);
        }

        if (TemplateMatcher.Split(path) is { } split
            && await pages.FindLiveTemplateAsync(site.Id, split.ParentPath, cancellationToken).ConfigureAwait(false) is { } template
            && await items.FindBySlugAsync(site.Id, template.CollectionId, split.Slug, cancellationToken).ConfigureAwait(false) is { } item)
        {
            var request = new ListRequest(path, PageParams.From(template.Params, query: null));
            return await responder.RenderItemAsync(site, template.PageId, item.ItemId, basePath, request, cancellationToken, state).ConfigureAwait(false);
        }

        return null;
    }
}
