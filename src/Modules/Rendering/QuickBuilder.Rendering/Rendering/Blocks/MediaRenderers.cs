using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using QuickBuilder.Rendering.Binding;
using QuickBuilder.Rendering.Domain;

namespace QuickBuilder.Rendering.Rendering.Blocks;

// Blocks that point somewhere: an image, a button, an embedded video. Each matches its canvas
// component in src/admin/src/editor/blocks/<type>/, except where page-schema.md §9 allows the
// published page to differ (a missing image, a link to a page that is not live, the embed).

/// <summary>An image from the site's media library (012 FR-027; responsive since 013 MV-15).</summary>
/// <remarks>
/// <para>
/// <b>Correctly sized</b> (contracts/public-media.md §2): <c>srcset</c> offers every resized
/// copy, and the original only when it is no wider than the largest copy, so a 6,000-pixel
/// photo is never downloaded (SC-003). <c>sizes</c> tells the browser how wide the image will
/// be: the enclosing Section's content width, or the image's own width when it is shown at
/// its natural size. <c>src</c> is a sensible default for browsers that ignore both.
/// </para>
/// <para>
/// <b>No layout shift</b>: <c>width</c> and <c>height</c> state the displayed size, so the
/// browser reserves the space before the bytes arrive (SC-006). They are left out while the
/// size is unknown (an image not measured yet).
/// </para>
/// </remarks>
internal sealed class ImageRenderer : IBlockRenderer
{
    private static readonly string[] Widths = ["full", "natural"];

    /// <summary>The widest copy used as <c>src</c>: enough for a desktop screen at 1×.</summary>
    internal const int DefaultSourceWidth = 1_280;

    public string Type => "image";

    public void Render(JsonElement node, string scope, RenderContext context, HtmlWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(writer);

        // A file that is gone takes the whole image with it: nothing broken is shown (FR-027).
        // 016: a bound image shows the item's file, or the block's own when it has none.
        var boundMedia = context.Bound(node, "media");
        Guid mediaId;
        if (boundMedia is { State: BoundState.Value, MediaId: { } boundId })
        {
            mediaId = boundId;
        }
        else if (context.Prop(node, "media") is not { ValueKind: JsonValueKind.Object } media
            || !media.TryGetProperty("mediaId", out var id)
            || !Guid.TryParse(id.GetString(), out mediaId))
        {
            return;
        }

        if (!context.Inputs.Media.TryGetValue(mediaId, out var file))
        {
            return;
        }

        var basePath = PageShell.TrimBase(context.Inputs.BasePath);
        var width = context.Choice(node, "width", Widths);
        var alt = context.Bool(node, "decorative") ? string.Empty : context.BoundText(node, "alt");
        var link = LinkResolver.Resolve(node, "link", context);
        var linked = link is not null && LinkResolver.IsWritable(link);

        writer.Open("figure").Attribute("class", $"qb-image qb-image--{width}").EndTag();

        if (linked)
        {
            writer.Open("a");
            LinkResolver.Write(writer, link!, newTab: false);
            writer.EndTag();
        }

        var source = file.Variants.LastOrDefault(v => v.Width <= DefaultSourceWidth)?.PublicPath ?? file.PublicPath;

        writer.Open("img");
        writer.SitePath("src", basePath + source);

        if (file.Variants.Count > 0)
        {
            writer.Attribute("srcset", SourceSet(file, basePath));
            writer.Attribute("sizes", Sizes(context.SectionWidth, width == "natural" ? file.Width : null));
        }

        if (file.Width is { } w && file.Height is { } h)
        {
            writer.Attribute("width", w.ToString(CultureInfo.InvariantCulture))
                .Attribute("height", h.ToString(CultureInfo.InvariantCulture));
        }

        writer.Attribute("alt", alt).Attribute("loading", "lazy").Attribute("decoding", "async").EndTag();

        if (linked)
        {
            writer.Close("a");
        }

        writer.Close("figure");
    }

    /// <summary>Every resized copy, and the original when it is no wider than the largest.</summary>
    internal static string SourceSet(QuickBuilder.Media.Contracts.PublicMediaFile file, string basePath)
    {
        ArgumentNullException.ThrowIfNull(file);

        var entries = file.Variants
            .Select(v => $"{basePath}{v.PublicPath} {v.Width.ToString(CultureInfo.InvariantCulture)}w")
            .ToList();

        if (file.OfferOriginal && file.Width is { } original)
        {
            entries.Add($"{basePath}{file.PublicPath} {original.ToString(CultureInfo.InvariantCulture)}w");
        }

        return string.Join(", ", entries);
    }

    /// <summary>
    /// How wide the image will be drawn: the Section's content width, capped by the image's own
    /// width when it is shown at its natural size; the whole viewport in a full-width Section.
    /// </summary>
    internal static string Sizes(double? sectionWidth, int? naturalWidth)
    {
        double? cap = (sectionWidth, naturalWidth) switch
        {
            ({ } s, { } n) => Math.Min(s, n),
            ({ } s, null) => s,
            (null, { } n) => n,
            _ => null,
        };

        if (cap is not { } px)
        {
            return "100vw";
        }

        var value = px.ToString("0.##", CultureInfo.InvariantCulture);
        return $"(max-width: {value}px) 100vw, {value}px";
    }
}

/// <summary>A button: a link when it goes somewhere visitors can open, text otherwise.</summary>
internal sealed class ButtonRenderer : IBlockRenderer
{
    private static readonly string[] Alignments = ["start", "center", "end"];
    private static readonly string[] Styles = ["primary", "secondary", "outline"];

    // ButtonBlock.vue: a button always takes the brand and background colours.
    private static readonly JsonElement Brand = JsonDocument.Parse("""{"token":"color-brand"}""").RootElement.Clone();
    private static readonly JsonElement Background = JsonDocument.Parse("""{"token":"color-background"}""").RootElement.Clone();

    public string Type => "button";

    public void Render(JsonElement node, string scope, RenderContext context, HtmlWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(writer);

        var link = LinkResolver.Resolve(node, "link", context);
        var tag = link is not null && LinkResolver.IsWritable(link) ? "a" : "span";

        writer.Open("div").Attribute("class", $"qb-button-row qb-align--{context.Choice(node, "align", Alignments)}").EndTag();
        writer.Open(tag).Attribute("class", $"qb-button qb-button--{context.Choice(node, "style", Styles)}");
        context.Style(writer, scope, node,
            ("--qb-button-brand", context.Tokens.Value(Brand, TokenKind.Color, Brand)),
            ("--qb-button-on-brand", context.Tokens.Value(Background, TokenKind.Color, Background)));

        if (tag == "a")
        {
            LinkResolver.Write(writer, link!, context.Bool(node, "newTab"));
        }

        writer.EndTag().Text(context.BoundText(node, "label")).Close(tag).Close("div");
    }
}

/// <summary>
/// A video from an allow-listed provider (012 research PR-11): the provider's privacy-enhanced
/// player, loaded only as it nears the viewport.
/// </summary>
internal sealed partial class EmbedRenderer : IBlockRenderer
{
    private static readonly string[] Ratios = ["16:9", "4:3", "1:1"];

    public string Type => "embed";

    public void Render(JsonElement node, string scope, RenderContext context, HtmlWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(writer);

        if (PlayerUrl(context.String(node, "url")) is not { } player)
        {
            return;
        }

        writer.Open("div").Attribute("class", "qb-embed");
        // The ratio is one of three constants, turned into CSS the way the canvas does it.
        context.Style(writer, scope, node, ("aspect-ratio", context.Choice(node, "aspectRatio", Ratios).Replace(":", " / ", StringComparison.Ordinal)));
        writer.EndTag();

        writer.Open("iframe");
        writer.Url("src", player);
        writer.Attribute("title", context.String(node, "title"))
            .Attribute("loading", "lazy")
            .Attribute("allow", "fullscreen; picture-in-picture")
            .Attribute("referrerpolicy", "strict-origin-when-cross-origin")
            .EndTag()
            .Close("iframe")
            .Close("div");
    }

    /// <summary>The player address for a supported video address, or null.</summary>
    public static string? PlayerUrl(string url)
    {
        var youtube = YouTube().Match(url);
        if (youtube.Success)
        {
            return $"https://www.youtube-nocookie.com/embed/{youtube.Groups["id"].Value}";
        }

        var vimeo = Vimeo().Match(url);
        return vimeo.Success ? $"https://player.vimeo.com/video/{vimeo.Groups["id"].Value}?dnt=1" : null;
    }

    // The page schema's own patterns for the two providers (docs/contracts/page-schema.md §2).
    [GeneratedRegex("^https://((www\\.)?youtube\\.com/watch\\?v=(?<id>[A-Za-z0-9_-]{11})(&[^\\u0000-\\u0020\\u007f]*)?|youtu\\.be/(?<id>[A-Za-z0-9_-]{11})(\\?[^\\u0000-\\u0020\\u007f]*)?)$", RegexOptions.CultureInvariant)]
    private static partial Regex YouTube();

    [GeneratedRegex("^https://(www\\.)?vimeo\\.com/(?<id>[0-9]{1,12})(\\?[^\\u0000-\\u0020\\u007f]*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex Vimeo();
}

/// <summary>
/// A shared component, drawn in place from its live version (012 FR-002, public-rendering.md §3).
/// </summary>
/// <remarks>
/// No wrapper element: the component's Sections sit in the page as if they were its own. Its
/// nodes get their own style scope, so a node id the component shares with the page never
/// collides. A component with no live version draws nothing; publishing refuses to reach that
/// state (FR-003), but the renderer does not rely on it.
/// </remarks>
internal sealed class ComponentRefRenderer : IBlockRenderer
{
    public string Type => "component-ref";

    public void Render(JsonElement node, string scope, RenderContext context, HtmlWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(writer);

        if (context.Prop(node, "componentId") is not { ValueKind: JsonValueKind.String } id
            || !Guid.TryParse(id.GetString(), out var componentId)
            || !context.Inputs.Components.TryGetValue(componentId, out var component)
            || !PageRenderer.SupportedSchemaVersions.Contains(component.SchemaVersion)
            || !component.Document.TryGetProperty("root", out var root))
        {
            return;
        }

        context.RenderChildren(root, $"c.{componentId:N}.", writer);
    }
}
