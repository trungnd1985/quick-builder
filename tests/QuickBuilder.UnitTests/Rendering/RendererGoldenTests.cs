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

    /// <summary>Renders an example in the fixed context of <c>tests/fixtures/rendering/context.json</c>.</summary>
    internal static string Render(string example)
    {
        using var context = JsonDocument.Parse(File.ReadAllText(RepositoryFiles.Fixtures("context.json")));
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryFiles.ValidExamples, $"{example}.json")));
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
            harness.Media[Guid.Parse(file.Name)] = new PublicMediaFile(
                file.Value.GetProperty("publicPath").GetString()!, file.Value.GetProperty("contentType").GetString()!);
        }

        foreach (var component in root.GetProperty("components").EnumerateObject())
        {
            harness.Components[Guid.Parse(component.Name)] = new LiveDocument(1, component.Value.Clone());
        }

        return harness.Page(document.RootElement.Clone(), root.GetProperty("title").GetString()!);
    }
}
