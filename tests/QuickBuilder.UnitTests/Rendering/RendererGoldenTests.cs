using System.Text.Json;
using QuickBuilder.Media.Contracts;
using QuickBuilder.Pages.Contracts.Publishing;

namespace QuickBuilder.UnitTests.Rendering;

/// <summary>
/// Every valid example document renders to its checked-in HTML, byte for byte (012 research
/// PR-10; page-schema.md §8).
/// </summary>
/// <remarks>
/// <c>CanvasMarkupParity.spec.ts</c> compares the editor canvas with the same files, so neither
/// side can change a block's markup without the other noticing. Set <c>QB_UPDATE_GOLDEN=1</c>
/// to rewrite them, then review every changed file before committing.
/// </remarks>
public sealed class RendererGoldenTests
{
    public static TheoryData<string> Examples() =>
        [.. Directory.GetFiles(RepositoryFiles.ValidExamples, "*.json").Select(f => Path.GetFileNameWithoutExtension(f)).Order(StringComparer.Ordinal)];

    [Theory]
    [MemberData(nameof(Examples))]
    public void The_rendered_page_equals_its_golden_file(string example)
    {
        var html = Render(example);
        var golden = RepositoryFiles.Fixtures("golden", $"{example}.html");

        if (RepositoryFiles.UpdateGolden)
        {
            File.WriteAllText(golden, html);
            return;
        }

        Assert.True(File.Exists(golden), $"No golden file for {example}. Run the tests with QB_UPDATE_GOLDEN=1, then review it.");
        Assert.Equal(File.ReadAllText(golden).ReplaceLineEndings("\n"), html);
    }

    /// <summary>FR-028: no published page built from the core blocks ships JavaScript.</summary>
    [Theory]
    [MemberData(nameof(Examples))]
    public void A_published_page_ships_no_javascript(string example)
    {
        var html = Render(example);

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch("<[^>]*\\son[a-z]+\\s*=", html);
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" style=", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// 012 SC-003: the page the render budget is measured on — 200 blocks of every type, with a
    /// header and a footer component — is valid and renders to its checked-in HTML.
    /// </summary>
    [Fact]
    public void The_two_hundred_block_page_is_valid_and_renders_to_its_golden_file()
    {
        var file = RepositoryFiles.Fixtures("page-200-blocks.json");
        Assert.True(new QuickBuilder.Pages.Infrastructure.Schema.PageDocumentValidator()
            .Validate(File.ReadAllBytes(file), QuickBuilder.Pages.Contracts.DocumentKind.Page).IsValid);

        using var parsed = JsonDocument.Parse(File.ReadAllText(file));
        Assert.Equal(200, Count(parsed.RootElement.GetProperty("root")));

        var html = RenderFile(file);
        var golden = RepositoryFiles.Fixtures("golden", "page-200-blocks.html");
        if (RepositoryFiles.UpdateGolden)
        {
            File.WriteAllText(golden, html);
            return;
        }

        Assert.Equal(File.ReadAllText(golden).ReplaceLineEndings("\n"), html);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);

        static int Count(JsonElement node) => node.GetProperty("children").EnumerateArray().Sum(c => 1 + Count(c));
    }

    /// <summary>Renders an example in the fixed context of <c>tests/fixtures/rendering/context.json</c>.</summary>
    internal static string Render(string example) =>
        RenderFile(Path.Combine(RepositoryFiles.ValidExamples, $"{example}.json"));

    /// <summary>Renders a document file in the fixed context.</summary>
    internal static string RenderFile(string path)
    {
        var (harness, document, title) = Prepare(path);
        return harness.Page(document, title);
    }

    /// <summary>A harness set up with the fixed context, and the document file read, ready to render.</summary>
    internal static (RenderHarness Harness, JsonElement Document, string Title) Prepare(string path)
    {
        using var context = JsonDocument.Parse(File.ReadAllText(RepositoryFiles.Fixtures("context.json")));
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = context.RootElement;

        var harness = new RenderHarness { BasePath = root.GetProperty("basePath").GetString()! };
        harness.Links.Clear();
        harness.Media.Clear();

        foreach (var target in root.GetProperty("linkTargets").EnumerateObject())
        {
            harness.Links[Guid.Parse(target.Name)] = new PageLinkTarget(
                target.Value.GetProperty("path").GetString()!, target.Value.GetProperty("isLive").GetBoolean());
        }

        foreach (var file in root.GetProperty("media").EnumerateObject())
        {
            var value = file.Value;
            int? Number(string name) => value.TryGetProperty(name, out var n) ? n.GetInt32() : null;

            // 013 MV-15: dimensions and variants, when the context gives them.
            var variants = value.TryGetProperty("variants", out var list)
                ? [.. list.EnumerateArray().Select(v => new PublicMediaVariant(v.GetProperty("width").GetInt32(), v.GetProperty("publicPath").GetString()!))]
                : new List<PublicMediaVariant>();

            harness.Media[Guid.Parse(file.Name)] = new PublicMediaFile(
                value.GetProperty("publicPath").GetString()!,
                value.GetProperty("contentType").GetString()!,
                Number("width"),
                Number("height"),
                variants,
                value.TryGetProperty("offerOriginal", out var offer) && offer.GetBoolean());
        }

        foreach (var component in root.GetProperty("components").EnumerateObject())
        {
            harness.Components[Guid.Parse(component.Name)] = new LiveDocument(1, component.Value.Clone());
        }

        return (harness, document.RootElement.Clone(), root.GetProperty("title").GetString()!);
    }
}
