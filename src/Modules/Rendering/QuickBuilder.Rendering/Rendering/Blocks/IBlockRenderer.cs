using System.Text.Json;
using QuickBuilder.Rendering.Domain;

namespace QuickBuilder.Rendering.Rendering.Blocks;

/// <summary>
/// Renders one block type to HTML (012 FR-023, research PR-09).
/// </summary>
/// <remarks>
/// One class per node type in the page schema, and nothing else knows how that type looks:
/// adding a block type adds one renderer and changes no other (constitution Principle VI).
/// <c>BlockRendererCoverageTests</c> fails the build when a schema type has none. The markup
/// matches the editor's canvas component for the same type (page-schema.md §9).
/// </remarks>
internal interface IBlockRenderer
{
    /// <summary>The node type this renders, as the schema names it.</summary>
    string Type { get; }

    /// <param name="node">The node.</param>
    /// <param name="scope">Prefix for the node's style key, unique per document on the page.</param>
    /// <param name="context">The render in progress.</param>
    /// <param name="writer">Where the HTML goes.</param>
    void Render(JsonElement node, string scope, RenderContext context, HtmlWriter writer);
}
