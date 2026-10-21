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

    /// <summary>A measured 6,000 × 4,000 photo with every default variant (013 MV-15).</summary>
    public static readonly Guid LargePhoto = Guid.Parse("44444444-4444-4444-8444-444444444444");

    /// <summary>A measured 800 × 400 logo: two variants, and small enough to offer itself.</summary>
    public static readonly Guid SmallLogo = Guid.Parse("55555555-5555-4555-8555-555555555555");

    public Dictionary<Guid, PublicMediaFile> Media { get; } = new()
    {
        // Not measured yet: no dimensions, no variants (as before the 013 backfill).
        [Photo] = new PublicMediaFile($"/_media/{Photo:N}.jpg", "image/jpeg", null, null, []),
        [LargePhoto] = new PublicMediaFile(
            $"/_media/{LargePhoto:N}.jpg",
            "image/jpeg",
            6_000,
            4_000,
            [.. new[] { 320, 640, 960, 1280, 1920, 2560 }.Select(w => new PublicMediaVariant(w, $"/_media/{LargePhoto:N}-v1-w{w}.jpg"))],
            OfferOriginal: false),
        [SmallLogo] = new PublicMediaFile(
            $"/_media/{SmallLogo:N}-v2.png",
            "image/png",
            800,
            400,
            [new PublicMediaVariant(320, $"/_media/{SmallLogo:N}-v2-w320.png"), new PublicMediaVariant(640, $"/_media/{SmallLogo:N}-v2-w640.png")],
            OfferOriginal: true),
    };

    public Dictionary<Guid, LiveDocument> Components { get; } = [];

    /// <summary>Live menus by id, for Navigation blocks (024).</summary>
    public Dictionary<Guid, LiveMenu> Menus { get; } = [];

    /// <summary>Published items menu entries point at (024).</summary>
    public Dictionary<Guid, QuickBuilder.Cms.Contracts.Publishing.PublishedItem> Items { get; } = [];

    /// <summary>Live template patterns by collection (024).</summary>
    public Dictionary<Guid, string> Patterns { get; } = [];

    /// <summary>The page being rendered and its address, for active state (024).</summary>
    public Guid? CurrentPageId { get; set; }

    public Guid? CurrentItemId { get; set; }

    public string Path { get; set; } = "/";

    public string BasePath { get; set; } = string.Empty;

    public string Language { get; set; } = "en";

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
        new(title, document, Design, Components, Links, Media, Language, BasePath, Path: Path,
            Menus: Menus, MenuItems: Items, ItemPatterns: Patterns, CurrentPageId: CurrentPageId, CurrentItemId: CurrentItemId);

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
