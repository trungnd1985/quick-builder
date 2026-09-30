using System.Text.Json;
using QuickBuilder.Editor.Contracts;
using QuickBuilder.Media.Contracts;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Rendering.Domain;
using QuickBuilder.Rendering.Rendering;
using QuickBuilder.Rendering.Rendering.Blocks;

namespace QuickBuilder.UnitTests.Rendering;

/// <summary>Renders single blocks and whole documents with chosen inputs, for renderer tests.</summary>
internal sealed class RenderHarness
{
    public static readonly Guid LivePage = Guid.Parse("11111111-1111-4111-8111-111111111111");
    public static readonly Guid DraftOnlyPage = Guid.Parse("22222222-2222-4222-8222-222222222222");
    public static readonly Guid Photo = Guid.Parse("33333333-3333-4333-8333-333333333333");

    public Dictionary<Guid, PageLinkTarget> Links { get; } = new()
    {
        [LivePage] = new PageLinkTarget("/contact", IsLive: true),
        [DraftOnlyPage] = new PageLinkTarget("/draft-only", IsLive: false),
    };

    public Dictionary<Guid, PublicMediaFile> Media { get; } = new()
    {
        [Photo] = new PublicMediaFile($"/_media/{Photo:N}.jpg", "image/jpeg", null, null, []),
    };

    public Dictionary<Guid, LiveDocument> Components { get; } = [];

    public string BasePath { get; set; } = string.Empty;

    public JsonElement Design { get; set; } = DefaultDesignTokens.Document;

    public static PageRenderer Renderer { get; } = new(BlockRendererCoverageTests.AllRenderers());

    /// <summary>One block's HTML and the style rules it produced.</summary>
    public (string Html, string Rules) Block(string nodeJson)
    {
        using var node = JsonDocument.Parse(nodeJson);
        var context = Context(Document("[]"));
        var writer = new HtmlWriter();

        var renderer = BlockRendererCoverageTests.AllRenderers().Single(r => r.Type == RenderContext.TypeOf(node.RootElement));
        renderer.Render(node.RootElement, PageRenderer.PageScope, context, writer);

        return (writer.ToString(), context.NodeRules);
    }

    /// <summary>A whole page from a document.</summary>
    public string Page(JsonElement document, string title = "Test page") =>
        System.Text.Encoding.UTF8.GetString(QuickBuilder.Rendering.Features.RenderPage.PageResponder.Render(Inputs(document, title), Renderer).Html);

    public RenderInputs Inputs(JsonElement document, string title = "Test page") =>
        new(title, document, Design, Components, Links, Media, "en", BasePath);

    private RenderContext Context(JsonElement document)
    {
        var context = new RenderContext(Inputs(document), BlockDefaults.Current);
        context.RenderChildren = (parent, scope, output) =>
        {
            if (parent.TryGetProperty("children", out var children))
            {
                foreach (var child in children.EnumerateArray())
                {
                    BlockRendererCoverageTests.AllRenderers().Single(r => r.Type == RenderContext.TypeOf(child)).Render(child, scope, context, output);
                }
            }
        };
        return context;
    }

    /// <summary>A version 1 document whose root holds <paramref name="childrenJson"/>.</summary>
    public static JsonElement Document(string childrenJson)
    {
        using var document = JsonDocument.Parse(
            $$$"""{"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":{{{childrenJson}}}}}""");
        return document.RootElement.Clone();
    }
}
