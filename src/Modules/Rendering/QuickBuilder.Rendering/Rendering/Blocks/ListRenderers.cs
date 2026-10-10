using System.Globalization;
using System.Text.Json;
using QuickBuilder.Rendering.Binding;
using QuickBuilder.Rendering.Domain;

namespace QuickBuilder.Rendering.Rendering.Blocks;

// The Collection list and its two slots (016 research TB-07; public-rendering.md §5). Each
// matches its canvas component in src/admin/src/editor/blocks/<type>/.

/// <summary>
/// A Collection list: its card once per listed item, each bound to that item, or its empty state
/// when nothing is listed (FR-022).
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

        var columns = Math.Clamp(context.Int(node, "columns", 3), 1, 4);
        writer.Open("div").Attribute("class", "qb-list");
        context.Style(writer, scope, node,
            ("--qb-cols", columns.ToString(CultureInfo.InvariantCulture)),
            ("--qb-list-gap", context.Token(node, "gap", TokenKind.Space)));
        writer.EndTag();

        var (card, empty) = Slots(node);
        var key = ResolvedData.ListKey(RenderContext.IdOf(node), context.Scopes.ItemIds());
        var items = context.Data.Lists.TryGetValue(key, out var page) ? page.Items : [];

        if (items.Count > 0 && card is { } cardNode)
        {
            writer.Markup("<ul class=\"qb-list__items\">");
            foreach (var item in items)
            {
                context.Scopes.Push(RenderContext.IdOf(node), item);
                context.RenderNode(cardNode, scope, writer);
                context.Scopes.Pop();
            }

            writer.Markup("</ul>");
        }
        else if (empty is { } emptyNode)
        {
            context.RenderNode(emptyNode, scope, writer);
        }

        writer.Close("div");
    }

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

        writer.Markup("<li class=\"qb-list__card\">");
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
