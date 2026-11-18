using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Cms.Contracts;
using QuickBuilder.Media.Contracts;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Rendering.Contracts;
using QuickBuilder.Rendering.Domain;
using QuickBuilder.Rendering.Features.RenderPage;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Rendering.Features.Preview;

/// <summary>
/// Previews for signed-in members (022 spec User Story 1, FR-001–FR-004; public-preview.md §2).
/// </summary>
/// <remarks>
/// <para>
/// <c>[AllowAnonymous]</c>, and access is checked here (research PV-09): the admin authorization
/// filter answers an anonymous request with a 401 problem, which would say the address is
/// real. Here an anonymous caller, a member of another site and an unknown id all get the same
/// not-found page (FR-004).
/// </para>
/// <para>
/// The site is the route's, never the <c>Host</c> header's: <c>/_preview</c> answers on every
/// host name, and the session cookie lives on the admin's (PV-05).
/// </para>
/// </remarks>
[ApiController]
// 022 PV-09: access is checked in the action so every refusal is the same not-found page.
[AllowAnonymous]
public sealed class MemberPreviewController(
    IAccessEvaluator access,
    ISiteDirectory sites,
    ISiteContextSetter siteContext) : ControllerBase
{
    private const string Base = "/_preview/sites/{siteId:guid}";

    [HttpGet(Base + "/pages/{pageId:guid}")]
    [HttpHead(Base + "/pages/{pageId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public Task<IActionResult> PageAsync(
        Guid siteId, Guid pageId, [FromQuery] int? version, [FromQuery] Guid? item, CancellationToken cancellationToken) =>
        PreviewAsync(siteId, PageContentCapabilities.View, new PreviewTarget(PreviewKinds.Page, pageId, version, item), cancellationToken);

    [HttpGet(Base + "/items/{itemId:guid}")]
    [HttpHead(Base + "/items/{itemId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> ItemAsync(
        Guid siteId, Guid itemId, [FromQuery] int? version, CancellationToken cancellationToken) =>
        PreviewAsync(siteId, ItemCapabilities.CollectionsView, new PreviewTarget(PreviewKinds.Item, itemId, version), cancellationToken);

    /// <summary>Any current media file of the site, for member previews (PV-11): members can browse the whole library.</summary>
    [HttpGet(Base + "/media/{**file}")]
    [HttpHead(Base + "/media/{**file}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MediaAsync(Guid siteId, string? file, [FromServices] IMediaContent media, CancellationToken cancellationToken)
    {
        if (!await MayViewAsync(siteId, PageContentCapabilities.View, cancellationToken).ConfigureAwait(false))
        {
            return new PublicResponseResult(NotAvailablePage.Gone);
        }

        var content = await media.OpenAsync(file ?? string.Empty, cancellationToken).ConfigureAwait(false);
        return content is null ? new PublicResponseResult(NotAvailablePage.Gone) : PreviewMedia.Result(this, content);
    }

    /// <summary>Previews are only read: anything but GET and HEAD is refused.</summary>
    [HttpPost(Base + "/{**rest}")]
    [HttpPut(Base + "/{**rest}")]
    [HttpPatch(Base + "/{**rest}")]
    [HttpDelete(Base + "/{**rest}")]
    [ProducesResponseType(StatusCodes.Status405MethodNotAllowed)]
    public IActionResult OtherMethod()
    {
        Response.Headers.Allow = "GET, HEAD";
        return StatusCode(StatusCodes.Status405MethodNotAllowed);
    }

    private async Task<IActionResult> PreviewAsync(
        Guid siteId, string capability, PreviewTarget target, CancellationToken cancellationToken)
    {
        if (!await MayViewAsync(siteId, capability, cancellationToken).ConfigureAwait(false)
            || await sites.FindByIdAsync(siteId, cancellationToken).ConfigureAwait(false) is not { } site)
        {
            return new PublicResponseResult(NotAvailablePage.Gone);
        }

        var responder = HttpContext.RequestServices.GetRequiredService<PreviewResponder>();
        var outcome = await responder.RespondAsync(
                new PreviewRequest(
                    site,
                    target,
                    PreviewMode.Member,
                    Request.PathBase.Value ?? string.Empty,
                    Request.Path.Value ?? string.Empty,
                    $"/_preview/sites/{siteId}/media",
                    Request.Query.ToDictionary(q => q.Key, q => (string?)q.Value.FirstOrDefault(), StringComparer.Ordinal),
                    ExpiresAt: null),
                cancellationToken)
            .ConfigureAwait(false);

        return new PublicResponseResult(outcome switch
        {
            PreviewOutcome.Rendered rendered => PreviewResponse.Page(rendered.Page),
            PreviewOutcome.NoTemplate => PreviewResponse.Fixed(StatusCodes.Status409Conflict, PreviewTexts.NoTemplateTitle, PreviewTexts.NoTemplateMessage),
            PreviewOutcome.InvalidDraft invalid => PreviewResponse.Fixed(
                StatusCodes.Status422UnprocessableEntity,
                PreviewTexts.InvalidDraftTitle,
                $"{invalid.Message} ({invalid.Location}). {PreviewTexts.InvalidDraftAdvice}"),
            _ => NotAvailablePage.Gone,
        });
    }

    /// <summary>
    /// A signed-in member of <paramref name="siteId"/> holding <paramref name="capability"/>; the
    /// site context is set to it for every read that follows.
    /// </summary>
    private async Task<bool> MayViewAsync(Guid siteId, string capability, CancellationToken cancellationToken)
    {
        if (!access.IsSignedIn || !await access.HasCapabilityAsync(capability, siteId, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        siteContext.SetSite(siteId);
        return true;
    }
}

/// <summary>A media file as a preview serves it (public-preview.md §5).</summary>
internal static class PreviewMedia
{
    public static IActionResult Result(ControllerBase controller, MediaFileContent content)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(content);

        var headers = controller.Response.Headers;
        foreach (var (name, value) in PreviewResponse.MediaHeaders(content.ETag, content.LastModifiedUtc))
        {
            headers[name] = value;
        }

        var disposition = new Microsoft.Net.Http.Headers.ContentDispositionHeaderValue("inline");
        disposition.SetHttpFileName(content.FileName);
        headers.ContentDisposition = disposition.ToString();
        if (content.IsImage)
        {
            headers.ContentSecurityPolicy = "default-src 'none'; img-src 'self'; style-src 'unsafe-inline'; sandbox";
        }

        return controller.File(content.Content, content.ContentType, enableRangeProcessing: true);
    }
}

/// <summary>
/// The block stylesheet for previews (022 research PV-06), which answer on every host name: the
/// same file <c>/_assets</c> serves on a site's host names, needing no site.
/// </summary>
[ApiController]
// Public: a stylesheet with no content of any site.
[AllowAnonymous]
public sealed class PreviewAssetsController : ControllerBase
{
    [HttpGet("/_preview/assets/{**file}")]
    [HttpHead("/_preview/assets/{**file}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult Get(string? file)
    {
        var stylesheet = Rendering.BlockStylesheet.Instance;
        if (!string.Equals(file, stylesheet.FileName, StringComparison.Ordinal))
        {
            return new PublicResponseResult(NotAvailablePage.Gone);
        }

        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        return File(stylesheet.Content, "text/css; charset=utf-8");
    }
}

/// <summary>
/// Any other address under <c>/_preview</c>: the same "no longer available" page every failed
/// link gets (022 research PV-10), so nothing under the prefix tells one miss from another.
/// </summary>
[ApiController]
// Public: it answers misses, with no content of any site.
[AllowAnonymous]
public sealed class PreviewFallbackController : ControllerBase
{
    [HttpGet("/_preview/{**rest}", Order = int.MaxValue)]
    [HttpHead("/_preview/{**rest}", Order = int.MaxValue)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult Get(string? rest)
    {
        // The address is never read or logged: it may carry a mistyped link secret (PV-07).
        _ = rest;
        Response.Headers.Allow = "GET, HEAD";
        return new PublicResponseResult(NotAvailablePage.Gone);
    }
}

/// <summary>
/// Where forms in a preview post (022 research PV-12, spec FR-026): nothing is read, stored,
/// counted or sent; the viewer is told it was a preview.
/// </summary>
[ApiController]
// Public: link viewers have no account, and nothing here touches any data.
[AllowAnonymous]
public sealed class PreviewFormController : ControllerBase
{
    [HttpPost("/_preview/form")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Post()
    {
        // Nothing of the request is read: not the body, not a form id (PV-12).
        HttpContext.Request.Body = Stream.Null;
        return new PublicResponseResult(NotAvailablePage.FormNotSent);
    }
}
