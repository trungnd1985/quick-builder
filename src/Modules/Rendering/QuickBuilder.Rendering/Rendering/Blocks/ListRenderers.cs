using System.Globalization;
using System.Text.Json;
using QuickBuilder.Cms.Contracts.Publishing;
using QuickBuilder.Rendering.Binding;
using QuickBuilder.Rendering.Domain;

namespace QuickBuilder.Rendering.Rendering.Blocks;

// The Collection list and its two slots (016 research TB-07; public-rendering.md §5). Each
// matches its canvas component in src/admin/src/editor/blocks/<type>/.

/// <summary>
/// A Collection list: <c>&lt;ul class="qb-list"&gt;</c> with its card once per listed item, each
/// bound to that item, or its empty state in place of the list when nothing is listed (FR-022;
/// public-rendering.md §5).
/// </summary>
/// <remarks>
/// The items were fetched before rendering (TB-09) and wait in <see cref="ResolvedData.Lists"/>
/// under the list's id and the items of the scopes around it, so a list inside a card shows its
/// own items for each card. A list whose result is missing — a broken source, or the render's
/// query budget spent — shows its empty state, never an error (FR-038).
/// </remarks>
internal sealed class CollectionListRenderer : IBlockRenderer
{
    public string Type => BindingNodeTypes.List;

    public void Render(JsonElement node, string scope, RenderContext context, HtmlWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(writer);

        var (card, empty) = Slots(node);
        var key = ResolvedData.ListKey(RenderContext.IdOf(node), context.Scopes.ItemIds());
        var result = context.Data.Lists.TryGetValue(key, out var page) ? page : null;
        var items = result?.Items ?? [];
        var paging = Paging(node, context);

        if (items.Count == 0 || card is not { } cardNode)
        {
            // public-rendering.md §5: the empty state renders in place of the list.
            if (empty is { } emptyNode)
            {
                context.RenderNode(emptyNode, scope, writer);
            }

            // US5 scenario 3: past the last page, the way back to the first.
            if (paging is { } pastEnd && pastEnd.Current > 1 && result is { Total: > 0 })
            {
                Pager.WriteBackToFirst(writer, context, pastEnd.Param, LayerLabel(node));
            }

            return;
        }

        // Columns step down to at most two at tablet width, and one on phones (blocks.css).
        var columns = Math.Clamp(context.Int(node, "columns", 3), 1, 4);
        writer.Open("ul").Attribute("class", "qb-list" + BorderStyle.Classes(node, context));
        context.Style(writer, scope, node, BorderStyle.With(node, context,
            ("--qb-cols", columns.ToString(CultureInfo.InvariantCulture)),
            ("--qb-cols-tablet", Math.Min(columns, 2).ToString(CultureInfo.InvariantCulture)),
            ("--qb-list-gap", context.Token(node, "gap", TokenKind.Space))));
        writer.EndTag();

        var listId = RenderContext.IdOf(node);
        foreach (var item in items)
        {
            context.Scopes.Push(listId, item);
            context.RenderNode(cardNode, scope, writer);
            context.Scopes.Pop();
        }

        writer.Close("ul");
        ListNode(items, context);

        if (paging is { } pages && result is not null)
        {
            Pager.Write(writer, context, pages.Param, LayerLabel(node), pages.Current, Pager.LastPage(result.Total, pages.PageSize), pages.Steps);
        }
    }

    /// <summary>
    /// 017 research SE-18: the shown items' pages, in order, when their collection has a live
    /// template; a list of items without pages describes nothing.
    /// </summary>
    private static void ListNode(IReadOnlyList<PublishedItem> items, RenderContext context)
    {
        if (context.Inputs.Site is not { } site)
        {
            return;
        }

        var elements = new System.Text.Json.Nodes.JsonArray();
        foreach (var item in items)
        {
            if (context.Data.TemplatePatterns.TryGetValue(item.CollectionId, out var pattern))
            {
                elements.Add(new System.Text.Json.Nodes.JsonObject
                {
                    ["@type"] = "ListItem",
                    ["position"] = elements.Count + 1,
                    ["url"] = Seo.SiteAddress.Absolute(site.BaseUrl, pattern.Replace(Pages.Contracts.TemplateInfo.SlugPlaceholder, item.Slug, StringComparison.Ordinal)),
                });
            }
        }

        if (elements.Count > 0)
        {
            context.StructuredData.Add(new System.Text.Json.Nodes.JsonObject { ["@type"] = "ItemList", ["itemListElement"] = elements });
        }
    }

    /// <summary>
    /// A paginated list's parameter, page size, current page and whether its pager shows
    /// Previous and Next only; null for a list without paging.
    /// </summary>
    private static (string Param, int PageSize, int Current, bool Steps)? Paging(JsonElement node, RenderContext context)
    {
        if (!node.TryGetProperty("props", out var props) || !props.TryGetProperty("paging", out var paging) || paging.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var param = ListQueries.PageParam(props);
        var pageSize = Math.Clamp(paging.TryGetProperty("pageSize", out var size) && size.TryGetInt32(out var s) ? s : ListQueryBuilder.DefaultLimit, 1, ListQueryBuilder.MaxLimit);
        var steps = paging.TryGetProperty("layout", out var layout) && layout.ValueKind == JsonValueKind.String && layout.GetString() == "steps";
        return (param, pageSize, context.Data.PageNumbers.TryGetValue(param, out var current) ? current : 1, steps);
    }

    /// <summary>The list's layer label, which names its pager for screen readers when set.</summary>
    private static string? LayerLabel(JsonElement node) =>
        node.TryGetProperty("props", out var props) && props.TryGetProperty("layerLabel", out var label)
        && label.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(label.GetString())
            ? label.GetString()
            : null;

    /// <summary>The list's card and empty state; rule 20 guarantees both, in that order.</summary>
    internal static (JsonElement? Card, JsonElement? Empty) Slots(JsonElement node)
    {
        JsonElement? card = null;
        JsonElement? empty = null;

        if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
            {
                switch (RenderContext.TypeOf(child))
                {
                    case BindingNodeTypes.Card:
                        card ??= child;
                        break;
                    case BindingNodeTypes.Empty:
                        empty ??= child;
                        break;
                }
            }
        }

        return (card, empty);
    }
}

/// <summary>One listed item's card; rendered by its list once per item, with the item's scope open.</summary>
internal sealed class ListCardRenderer : IBlockRenderer
{
    public string Type => BindingNodeTypes.Card;

    public void Render(JsonElement node, string scope, RenderContext context, HtmlWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(writer);

        // 066: a bordered card frames every listed item, all from the card node's one rule.
        writer.Open("li").Attribute("class", "qb-list__card" + BorderStyle.Classes(node, context));
        context.Style(writer, scope, node, BorderStyle.Declarations(node, context));
        writer.EndTag();
        context.RenderChildren(node, scope, writer);
        writer.Markup("</li>");
    }
}

/// <summary>A list's empty state; rendered by its list when it has nothing to show.</summary>
internal sealed class ListEmptyRenderer : IBlockRenderer
{
    public string Type => BindingNodeTypes.Empty;

    public void Render(JsonElement node, string scope, RenderContext context, HtmlWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(writer);

        writer.Markup("<div class=\"qb-list__empty\">");
        context.RenderChildren(node, scope, writer);
        writer.Markup("</div>");
    }
}

/// <summary>The node types bindings bring (page-schema.md §2, 016).</summary>
internal static class BindingNodeTypes
{
    public const string List = "collection-list";
    public const string Card = "list-card";
    public const string Empty = "list-empty";
}
