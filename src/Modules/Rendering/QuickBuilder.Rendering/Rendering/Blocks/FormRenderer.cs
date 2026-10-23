using System.Text.Json;
using QuickBuilder.Rendering.Domain;
using QuickBuilder.Rendering.Rendering.Forms;

namespace QuickBuilder.Rendering.Rendering.Blocks;

/// <summary>
/// A Form block (018 FR-001, FR-009; public-forms.md §2.1). The markup is
/// <see cref="FormMarkup"/>'s; this class is the block's one renderer (012 FR-023).
/// </summary>
internal sealed class FormRenderer : IBlockRenderer
{
    public string Type => "form";

    public void Render(JsonElement node, string scope, RenderContext context, HtmlWriter writer) =>
        FormMarkup.Write(node, scope, context, writer);
}
