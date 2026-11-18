using System.Globalization;
using QuickBuilder.Rendering.Rendering;

namespace QuickBuilder.Rendering.Seo;

/// <summary>
/// Writes the head tags of <see cref="HeadMetadata"/> in the order of public-seo.md §3 (017
/// research SE-05).
/// </summary>
/// <remarks>
/// Every value goes through <see cref="HtmlWriter.Attribute"/> or <see cref="HtmlWriter.Text"/>,
/// so nothing a member typed can end an attribute or the title (constitution Principle IX).
/// A tag whose value is empty is not written.
/// </remarks>
internal static class HeadWriter
{
    public static void Write(HtmlWriter writer, HeadMetadata head)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(head);

        writer.Markup("<title>").Text(head.Title).Markup("</title>\n");
        Meta(writer, "name", "description", head.Description);
        writer.Open("link").Attribute("rel", "canonical").Attribute("href", head.Canonical).EndTag().Markup("\n");
        Meta(writer, "name", "robots", head.Robots);

        // 022 research PV-10: a preview passes its address to no one.
        if (head.NoReferrer)
        {
            Meta(writer, "name", "referrer", "no-referrer");
        }

        Meta(writer, "property", "og:type", head.OgType);
        Meta(writer, "property", "og:title", head.OgTitle);
        Meta(writer, "property", "og:description", head.OgDescription);
        Meta(writer, "property", "og:url", head.Canonical);
        Meta(writer, "property", "og:site_name", head.SiteName);
        Meta(writer, "property", "og:locale", head.OgLocale);

        if (head.Image is { } image)
        {
            Meta(writer, "property", "og:image", image.Url);
            Meta(writer, "property", "og:image:width", image.Width?.ToString(CultureInfo.InvariantCulture));
            Meta(writer, "property", "og:image:height", image.Height?.ToString(CultureInfo.InvariantCulture));
            Meta(writer, "property", "og:image:alt", image.Alt);
        }
    }

    private static void Meta(HtmlWriter writer, string attribute, string name, string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        writer.Open("meta").Attribute(attribute, name).Attribute("content", content).EndTag().Markup("\n");
    }
}
