using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Editor.Contracts;
using QuickBuilder.Media.Contracts;
using QuickBuilder.Rendering.Contracts;
using QuickBuilder.Rendering.Domain;
using QuickBuilder.Rendering.Features.RenderPage;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Kernel.Routing;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Rendering.Features.Preview;

/// <summary>
/// Previews opened by a link, without an account (022 spec User Story 2, FR-006–FR-013,
/// FR-027; public-preview.md §3).
/// </summary>
/// <remarks>
/// <para>
/// The link is resolved on every request (research PV-08): a revoked or expired link, one whose
/// creator lost access, or one whose target is gone answers the same "no longer available" page,
/// byte for byte (FR-010). The address holds only the secret (FR-007); the site comes from the
/// link, never from the <c>Host</c> header, and the path is never logged.
/// </para>
/// <para>
/// A link shows exactly its own target: the draft, always the latest saved one (clarification
/// 1), or its one version. Media are those the target uses (PV-11); links in the page lead to
/// the public site (FR-012).
/// </para>
/// </remarks>
[ApiController]
// 022 PV-07: opened by its secret, without an account.
[AllowAnonymous]
public sealed class LinkPreviewController(IPreviewLinks links, ISiteDirectory sites, ISiteContextSetter siteContext) : ControllerBase
{
    private const string Base = PublicRoutes.Preview + "/l/{secret}";

    [HttpGet(Base)]
    [HttpHead(Base)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetAsync(string? secret, CancellationToken cancellationToken)
    {
        secret ??= string.Empty;
        var (refusal, found, site) = await ResolveAsync(secret, cancellationToken).ConfigureAwait(false);
        if (refusal is not null)
        {
            return new PublicResponseResult(refusal);
        }

        var outcome = await RenderAsync(secret, found!, site!, cancellationToken).ConfigureAwait(false);
        if (outcome is PreviewOutcome.Rendered rendered)
        {
            await HttpContext.RequestServices.GetRequiredService<PreviewMediaSet>()
                .RememberAsync(site!.Id, found!.LinkId, rendered.Media, cancellationToken)
                .ConfigureAwait(false);
        }

        return new PublicResponseResult(outcome switch
        {
            PreviewOutcome.Rendered page => PreviewResponse.Page(page.Page),
            PreviewOutcome.InvalidDraft => NotAvailablePage.Later,
            _ => NotAvailablePage.Gone,
        });
    }

    /// <summary>A media file the link's target uses; anything else is the same "no longer available" page.</summary>
    [HttpGet(Base + "/media/{**file}")]
    [HttpHead(Base + "/media/{**file}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MediaAsync(string? secret, string? file, [FromServices] IMediaContent media, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(media);

        secret ??= string.Empty;
        var (refusal, found, site) = await ResolveAsync(secret, cancellationToken).ConfigureAwait(false);
        if (refusal is not null)
        {
            return new PublicResponseResult(refusal);
        }

        var content = await media.OpenAsync(file ?? string.Empty, cancellationToken).ConfigureAwait(false);
        if (content is null)
        {
            return new PublicResponseResult(NotAvailablePage.Gone);
        }

        var uses = await HttpContext.RequestServices.GetRequiredService<PreviewMediaSet>()
            .ContainsAsync(
                site!.Id,
                found!.LinkId,
                content.FileId,
                async ct => await RenderAsync(secret, found, site, ct).ConfigureAwait(false) is PreviewOutcome.Rendered rendered
                    ? rendered.Media
                    : new HashSet<Guid>(),
                cancellationToken)
            .ConfigureAwait(false);
        if (!uses)
        {
            await content.DisposeAsync().ConfigureAwait(false);
            return new PublicResponseResult(NotAvailablePage.Gone);
        }

        return PreviewMedia.Result(this, content);
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

    /// <summary>The link's target and site, or the page that refuses it.</summary>
    private async Task<(PublicResponse? Refusal, PreviewLinkResolution.Found? Found, SiteDto? Site)> ResolveAsync(
        string secret, CancellationToken cancellationToken)
    {
        var resolution = await links.ResolveAsync(secret, HttpContext.Connection.RemoteIpAddress, cancellationToken).ConfigureAwait(false);
        switch (resolution)
        {
            case PreviewLinkResolution.Throttled throttled:
                return (NotAvailablePage.TooMany(throttled.RetryAfter), null, null);
            case PreviewLinkResolution.Found found
                when await sites.FindByIdAsync(found.SiteId, cancellationToken).ConfigureAwait(false) is { } site:
                siteContext.SetSite(site.Id);
                return (null, found, site);
            default:
                return (NotAvailablePage.Gone, null, null);
        }
    }

    private Task<PreviewOutcome> RenderAsync(string secret, PreviewLinkResolution.Found found, SiteDto site, CancellationToken cancellationToken) =>
        HttpContext.RequestServices.GetRequiredService<PreviewResponder>().RespondAsync(
            new PreviewRequest(
                site,
                new PreviewTarget(found.DocumentKind, found.DocumentId, found.VersionNumber, found.SampleItemId),
                PreviewMode.Link,
                Request.PathBase.Value ?? string.Empty,
                $"{PublicRoutes.Preview}/l/{secret}",
                $"{PublicRoutes.Preview}/l/{secret}/media",
                Request.Query.ToDictionary(q => q.Key, q => (string?)q.Value.FirstOrDefault(), StringComparer.Ordinal),
                found.ExpiresAtUtc),
            cancellationToken);
}
