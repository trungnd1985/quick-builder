using System.Globalization;
using System.Text.Json;
using QuickBuilder.Rendering.Domain;
using QuickBuilder.Rendering.Features.NotFound;
using QuickBuilder.Rendering.Rendering.Navigation;

namespace QuickBuilder.Rendering.Rendering.Blocks;

/// <summary>
/// A Navigation block: one of the site's live menus as a labelled navigation region (024
/// FR-016 to FR-020, FR-030; research NV-10 to NV-15; public-navigation.md §4).
/// </summary>
/// <remarks>
/// <para>
/// Each entry with shown children has its own open/close button, a <c>summary</c> beside the
/// entry's link, so the disclosure works by click, tap, Enter and Space without any script, and
/// the link still goes to its page (clarification 4). An entry without a link uses its label as
/// that button. Sibling submenus of one level share a <c>name</c>, so opening one closes the
/// others where browsers support it.
/// </para>
/// <para>
/// A collapsing menu is wrapped in a panel written open, so without scripts everything stays
/// visible; the stylesheet shows its button below the block's width, and the script closes the
/// panel there. In a vertical block the submenus that contain the current page start open; in a
/// horizontal block none do, and their entries are marked for the script (FR-019a).
/// </para>
/// <para>
/// A menu that is missing or not live writes nothing, and so does one with nothing to show
/// (FR-024): no empty navigation region is left in the page.
/// </para>
/// </remarks>
internal sealed class NavigationRenderer : IBlockRenderer
{
    private static readonly string[] Layouts = ["horizontal", "vertical"];
    private static readonly string[] Alignments = ["start", "center", "end"];
    private static readonly string[] Widths = ["mobile", "tablet"];

    public string Type => "navigation";

    public void Render(JsonElement node, string scope, RenderContext context, HtmlWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(writer);

        var inputs = context.Inputs;
        if (context.Prop(node, "menuId") is not { ValueKind: JsonValueKind.String } id
            || !Guid.TryParse(id.GetString(), out var menuId)
            || inputs.Menus is not { } menus
            || !menus.TryGetValue(menuId, out var menu)
            || !MenuModel.SupportedSchemaVersions.Contains(menu.SchemaVersion))
        {
            return;
        }

        var entries = MenuModel.Resolve(menu.Document, inputs);
        ActiveState.Mark(entries, new CurrentPage(inputs.CurrentPageId, inputs.CurrentItemId, inputs.Path));
        entries = MenuModel.Prune(entries, Math.Clamp(context.Int(node, "levels", 3), 1, 3));
        if (entries.Count == 0)
        {
            return;
        }

        var texts = PublicTexts.For(inputs.Language).Navigation;
        var layout = context.Choice(node, "layout", Layouts);
        var collapse = context.Prop(node, "collapse") is { ValueKind: JsonValueKind.Object } c && c.TryGetProperty("enabled", out var on) && on.ValueKind == JsonValueKind.True
            ? c
            : (JsonElement?)null;
        var below = collapse is { } settings && settings.TryGetProperty("below", out var b) && Array.IndexOf(Widths, b.GetString()) >= 0 ? b.GetString()! : Widths[0];
        var block = context.NextNavigationId();
        var label = context.String(node, "ariaLabel") is { Length: > 0 } own ? own : menu.Name;

        writer.Open("nav")
            .Attribute("class", $"qb-nav qb-nav--{layout} qb-nav--align-{context.Choice(node, "align", Alignments)}{(collapse is null ? string.Empty : $" qb-nav--collapse-{below}")}");
        if (label.Length > 0)
        {
            writer.Attribute("aria-label", label);
        }

        writer.Attribute("data-qb-nav", block);
        if (collapse is not null)
        {
            writer.Attribute("data-qb-collapse", below);
        }

        context.Style(writer, scope, node,
            ("--qb-nav-color", context.Token(node, "color", TokenKind.Color)),
            ("--qb-nav-active", context.Token(node, "activeColor", TokenKind.Color)));
        writer.EndTag();

        if (collapse is { } panel)
        {
            var button = panel.TryGetProperty("buttonLabel", out var text) && text.GetString() is { Length: > 0 } custom ? custom : texts.Menu;
            writer.Markup("<details class=\"qb-nav__panel\" open><summary class=\"qb-nav__toggle\">").Text(button).Markup("</summary>");
        }

        List(entries, 1, block, layout == "vertical", texts, writer);

        if (collapse is not null)
        {
            writer.Markup("</details>");
        }

        writer.Close("nav");

        // NV-14: once per page, right after the first menu, so a collapsing one closes before it is painted.
        if (!context.NavigationScriptWritten)
        {
            writer.Markup(NavScript.Element);
            context.NavigationScriptWritten = true;
        }
    }

    private static void List(List<ResolvedEntry> entries, int level, string block, bool vertical, NavigationTexts texts, HtmlWriter writer)
    {
        writer.Open("ul").Attribute("class", string.Create(CultureInfo.InvariantCulture, $"qb-nav__list qb-nav__list--l{level}")).EndTag();

        foreach (var entry in entries)
        {
            var leads = entry.State == NavState.Containing && entry.Children.Count > 0;
            writer.Open("li").Attribute("class", "qb-nav__item" + entry.State switch
            {
                NavState.Current => " qb-nav__item--current",
                NavState.Containing => " qb-nav__item--containing",
                NavState.Section => " qb-nav__item--section",
                _ => string.Empty,
            });
            if (leads)
            {
                writer.Attribute("data-qb-current-path", string.Empty);
            }

            writer.EndTag();

            var linked = entry.Href is not null && Link(entry, texts, writer);
            if (!linked && entry.Children.Count == 0)
            {
                // An address the writer refused, and nothing under it: the label alone.
                writer.Markup("<span class=\"qb-nav__text\">").Text(entry.Label).Markup("</span>");
            }

            if (entry.Children.Count > 0)
            {
                writer.Open("details").Attribute("class", "qb-nav__sub")
                    .Attribute("name", string.Create(CultureInfo.InvariantCulture, $"qb-{block}-l{level}"));
                if (vertical && leads)
                {
                    writer.Markup(" open");
                }

                writer.EndTag();
                if (linked)
                {
                    writer.Markup("<summary class=\"qb-nav__expander\"><span class=\"qb-nav__hidden\">")
                        .Text(string.Format(CultureInfo.InvariantCulture, texts.ShowSubmenu, entry.Label))
                        .Markup("</span></summary>");
                }
                else
                {
                    writer.Markup("<summary class=\"qb-nav__expander qb-nav__expander--label\">").Text(entry.Label).Markup("</summary>");
                }

                List(entry.Children, level + 1, block, vertical, texts, writer);
                writer.Markup("</details>");
            }

            writer.Close("li");
        }

        writer.Close("ul");
    }

    /// <summary>Writes the entry's link; false, writing nothing, when its address is not one a page may hold.</summary>
    private static bool Link(ResolvedEntry entry, NavigationTexts texts, HtmlWriter writer)
    {
        var href = entry.Href!;
        var allowed = entry.External ? new HtmlWriter().Url("href", href) : href.StartsWith('#') || HtmlWriter.IsSitePath(href);
        if (!allowed)
        {
            return false;
        }

        writer.Open("a").Attribute("class", "qb-nav__link").Attribute("href", href);
        if (entry.State == NavState.Current)
        {
            writer.Attribute("aria-current", "page");
        }

        if (entry.NewTab)
        {
            // 012 FR-025: the opened page gets no handle on this one.
            writer.Attribute("target", "_blank").Attribute("rel", "noopener noreferrer");
        }

        writer.EndTag().Text(entry.Label);
        if (entry.NewTab)
        {
            writer.Markup("<span class=\"qb-nav__hidden\"> ").Text(texts.NewTab).Markup("</span>");
        }

        writer.Close("a");
        return true;
    }
}
