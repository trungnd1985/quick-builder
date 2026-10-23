using System.Collections.Frozen;
using System.Text.Json;
using QuickBuilder.Rendering.Domain;
using QuickBuilder.Rendering.Rendering.Blocks;

namespace QuickBuilder.Rendering.Rendering;

/// <summary>
/// Walks a published document and hands each node to its type's renderer (012 research PR-09).
/// </summary>
/// <remarks>
/// A node of a type no renderer knows is skipped rather than failing the page: the document
/// was validated at publish, so this only happens if a schema version is newer than the
/// renderer, which <c>RendererSchemaVersionsTests</c> prevents from merging.
/// </remarks>
internal sealed class PageRenderer(IEnumerable<IBlockRenderer> renderers)
{
    /// <summary>The page schema versions this renderer renders (FR-029).</summary>
    /// <remarks>
    /// 019 RL-19: only the current one. Pages reads every stored document through its upgrade
    /// chain before handing it over, so an older version reaches the renderer upgraded, and one
    /// that cannot be used arrives with a version not listed here and fails safely.
    /// </remarks>
    public static IReadOnlySet<int> SupportedSchemaVersions { get; } = new HashSet<int> { QuickBuilder.Pages.Contracts.PageSchema.CurrentVersion };

    /// <summary>The style scope of the page's own nodes; components get their own.</summary>
    public const string PageScope = "p.";

    private readonly FrozenDictionary<string, IBlockRenderer> _renderers =
        renderers.ToFrozenDictionary(r => r.Type, StringComparer.Ordinal);

    /// <summary>The node types a renderer is registered for.</summary>
    public IEnumerable<string> Types => _renderers.Keys;

    /// <summary>Renders the document's root children into <paramref name="writer"/>.</summary>
    public void RenderDocument(JsonElement document, string scope, RenderContext context, HtmlWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!document.TryGetProperty("schemaVersion", out var version)
            || !version.TryGetInt32(out var number)
            || !SupportedSchemaVersions.Contains(number)
            || !document.TryGetProperty("root", out var root))
        {
            return;
        }

        context.RenderChildren = (parent, childScope, output) => RenderChildren(parent, childScope, context, output);
        context.RenderNode = (node, nodeScope, output) => RenderNode(node, nodeScope, context, output);
        RenderChildren(root, scope, context, writer);
    }

    private void RenderChildren(JsonElement parent, string scope, RenderContext context, HtmlWriter writer)
    {
        if (!parent.TryGetProperty("children", out var children) || children.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var child in children.EnumerateArray())
        {
            RenderNode(child, scope, context, writer);
        }
    }

    private void RenderNode(JsonElement node, string scope, RenderContext context, HtmlWriter writer)
    {
        // 016 FR-019: a block whose binding has nothing to show, with "hide" chosen, is left out.
        if (_renderers.TryGetValue(RenderContext.TypeOf(node), out var renderer) && !context.IsHidden(node))
        {
            renderer.Render(node, scope, context, writer);
        }
    }
}
