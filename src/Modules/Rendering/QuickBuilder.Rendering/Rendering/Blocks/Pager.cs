using System.Globalization;
using QuickBuilder.Rendering.Domain;
using QuickBuilder.Rendering.Features.NotFound;

namespace QuickBuilder.Rendering.Rendering.Blocks;

/// <summary>
/// A paginated list's navigation (016 research TB-11; public-rendering.md §3): plain links, no
/// script, so every page has an address that can be shared and works without JavaScript.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The <c>steps</c> layout shows Previous and Next only; the default shows numbers too.</item>
/// <item>At most 7 numbered links: the first, the last, and the current page with up to two
/// neighbours on each side, with <c>…</c> between gaps.</item>
/// <item>The current page has <c>aria-current="page"</c>; Previous and Next carry <c>rel</c>.</item>
/// <item>Every link keeps the page numbers of the page's other lists, so lists page
/// independently (FR-027). Page 1 drops its own parameter, so it shares the plain address.</item>
/// </list>
/// Labels are the site language's public texts.
/// </remarks>
internal static class Pager
{
    /// <summary>How many pages a list has, at least 1.</summary>
    public static int LastPage(int total, int pageSize) => Math.Max(1, (total + pageSize - 1) / pageSize);

    /// <summary>The page numbers shown, with 0 for a gap.</summary>
    public static IReadOnlyList<int> Numbers(int current, int last)
    {
        var shown = new SortedSet<int> { 1, last };
        for (var page = current - 2; page <= current + 2; page++)
        {
            if (page >= 1 && page <= last)
            {
                shown.Add(page);
            }
        }

        var numbers = new List<int>();
        var previous = 0;
        foreach (var page in shown)
        {
            if (page - previous > 1)
            {
                numbers.Add(0);
            }

            numbers.Add(page);
            previous = page;
        }

        return numbers;
    }

    /// <summary>The address of page <paramref name="page"/> of the list reading <paramref name="param"/>.</summary>
    public static string Href(RenderContext context, string param, int page)
    {
        ArgumentNullException.ThrowIfNull(context);

        var pairs = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (name, number) in context.Data.PageNumbers)
        {
            pairs[name] = number;
        }

        if (page > 1)
        {
            pairs[param] = page;
        }
        else
        {
            pairs.Remove(param);
        }

        var query = string.Join('&', pairs.Select(p => Uri.EscapeDataString(p.Key) + "=" + p.Value.ToString(CultureInfo.InvariantCulture)));
        // 022 research PV-06: a preview's pager stays in the preview.
        var path = context.Inputs.BasePath.TrimEnd('/') + (context.Inputs.Preview?.Addresses.PagerPath ?? context.Data.ListPath);
        return query.Length == 0 ? path : $"{path}?{query}";
    }

    /// <summary>
    /// The pager for page <paramref name="current"/> of <paramref name="last"/>; nothing for a
    /// single page. With <paramref name="steps"/>, Previous and Next only, without numbers (FR-027).
    /// </summary>
    public static void Write(HtmlWriter writer, RenderContext context, string param, string? label, int current, int last, bool steps = false)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(context);

        if (last <= 1)
        {
            return;
        }

        var texts = PublicTexts.For(context.Inputs.Language).Pager;
        Open(writer, label ?? texts.Label, steps);

        if (current > 1)
        {
            Link(writer, Href(context, param, current - 1), texts.Previous, rel: "prev");
        }

        foreach (var page in steps ? Array.Empty<int>() : Numbers(current, last))
        {
            if (page == 0)
            {
                writer.Markup("<span class=\"qb-pager__gap\" aria-hidden=\"true\">…</span>");
                continue;
            }

            writer.Open("a").Attribute("class", "qb-pager__page").Attribute("href", Href(context, param, page));
            if (page == current)
            {
                writer.Attribute("aria-current", "page");
            }

            writer.EndTag().Text(page.ToString(CultureInfo.InvariantCulture)).Close("a");
        }

        if (current < last)
        {
            Link(writer, Href(context, param, current + 1), texts.Next, rel: "next");
        }

        writer.Close("nav");
    }

    /// <summary>Past the last page: the way back to the first (US5 scenario 3).</summary>
    public static void WriteBackToFirst(HtmlWriter writer, RenderContext context, string param, string? label)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(context);

        var texts = PublicTexts.For(context.Inputs.Language).Pager;
        Open(writer, label ?? texts.Label);
        Link(writer, Href(context, param, 1), texts.First, rel: null);
        writer.Close("nav");
    }

    private static void Open(HtmlWriter writer, string label, bool steps = false) =>
        writer.Open("nav").Attribute("class", steps ? "qb-pager qb-pager--steps" : "qb-pager").Attribute("aria-label", label).EndTag();

    private static void Link(HtmlWriter writer, string href, string text, string? rel)
    {
        writer.Open("a").Attribute("class", "qb-pager__step").Attribute("href", href);
        if (rel is not null)
        {
            writer.Attribute("rel", rel);
        }

        writer.EndTag().Text(text).Close("a");
    }
}
