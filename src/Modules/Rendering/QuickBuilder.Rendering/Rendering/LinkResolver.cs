using System.Text.Json;
using QuickBuilder.Rendering.Domain;

namespace QuickBuilder.Rendering.Rendering;

/// <summary>
/// Where a stored link goes on the published site, if anywhere (012 FR-026, research PR-11).
/// </summary>
/// <remarks>
/// A link to a page of the site follows the page: it is written with the page's current
/// address, root-relative under the base path, so a move or rename never breaks it. A link to
/// a page that is not live, or no longer exists, goes nowhere — the caller writes the text
/// without a link, so visitors never follow the site's own content into a not-found page. An
/// external address is used only with a scheme the page schema allows.
/// </remarks>
internal static class LinkResolver
{
    /// <summary>The address to link to, or null when the link must not be written.</summary>
    public static ResolvedLink? Resolve(JsonElement? link, RenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (link is not { ValueKind: JsonValueKind.Object } value || !value.TryGetProperty("type", out var type))
        {
            return null;
        }

        switch (type.GetString())
        {
            case "page":
                if (value.TryGetProperty("pageId", out var id)
                    && Guid.TryParse(id.GetString(), out var pageId)
                    && context.Inputs.LinkTargets.TryGetValue(pageId, out var target)
                    && target.IsLive)
                {
                    var href = PageShell.TrimBase(context.Inputs.BasePath) + target.Path;
                    return HtmlWriter.IsSitePath(href) ? new ResolvedLink(href, External: false) : null;
                }

                return null;

            case "url":
                return value.TryGetProperty("href", out var external) && external.GetString() is { } address
                    ? new ResolvedLink(address, External: true)
                    : null;

            default:
                return null;
        }
    }

    /// <summary>Writes <c>href</c> (and, for a new tab, <c>target</c> and <c>rel</c>). False when nothing was written.</summary>
    public static bool Write(HtmlWriter writer, ResolvedLink link, bool newTab)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(link);

        var written = link.External ? writer.Url("href", link.Href) : writer.SitePath("href", link.Href);
        if (written && newTab)
        {
            // FR-025: the opened page gets no handle on this one.
            writer.Attribute("target", "_blank").Attribute("rel", "noopener noreferrer");
        }

        return written;
    }

    /// <summary>Whether <see cref="Write"/> would write this link.</summary>
    public static bool IsWritable(ResolvedLink link)
    {
        ArgumentNullException.ThrowIfNull(link);
        return link.External ? new HtmlWriter().Url("href", link.Href) : HtmlWriter.IsSitePath(link.Href);
    }
}

/// <summary>A link's address, and whether it leaves the site.</summary>
internal sealed record ResolvedLink(string Href, bool External);
