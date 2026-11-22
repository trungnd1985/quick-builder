using QuickBuilder.Rendering.Domain;

namespace QuickBuilder.Rendering.Rendering;

/// <summary>
/// How a render links to a page or item page of its own site (012 FR-026; 022 research PV-06).
/// </summary>
/// <remarks>
/// A published page writes the address root-relative under the base path. A preview writes the
/// public, absolute address on the site's host name, so a viewer following it lands on the
/// published site, never on another draft and never on the admin host name a member preview is
/// served from (spec FR-012). A site without a host name has no public address: the link is not
/// written, and the caller shows its text without a link.
/// </remarks>
internal static class PageLinks
{
    /// <summary>The link to <paramref name="path"/> (a normalised page address), or null when none may be written.</summary>
    public static ResolvedLink? To(RenderInputs inputs, string path)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(path);

        if (inputs.Preview is { } preview)
        {
            if (preview.Addresses.PublicSiteUrl is not { } siteUrl)
            {
                return null;
            }

            var absolute = siteUrl.TrimEnd('/') + path;
            return new HtmlWriter().Url("href", absolute) ? new ResolvedLink(absolute, External: true) : null;
        }

        var href = PageShell.TrimBase(inputs.BasePath) + path;
        return HtmlWriter.IsSitePath(href) ? new ResolvedLink(href, External: false) : null;
    }
}
